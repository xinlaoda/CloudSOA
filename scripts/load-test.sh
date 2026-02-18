#!/usr/bin/env bash
# =============================================================================
# CloudSOA 简单负载测试脚本
# 用法: ./scripts/load-test.sh [BROKER_URL] [--requests N] [--concurrency N]
# =============================================================================
set -euo pipefail

BROKER_URL="${1:-http://localhost:5000}"
TOTAL_REQUESTS=100
CONCURRENCY=5

shift || true
while [[ $# -gt 0 ]]; do
    case $1 in
        --requests)    TOTAL_REQUESTS="$2"; shift 2 ;;
        --concurrency) CONCURRENCY="$2"; shift 2 ;;
        *) shift ;;
    esac
done

BATCH_SIZE=$((TOTAL_REQUESTS / CONCURRENCY))
PAYLOAD=$(echo -n "load-test-payload-data-1234567890" | base64)

echo "============================================"
echo "  CloudSOA 负载测试"
echo "  Broker:   ${BROKER_URL}"
echo "  总请求:    ${TOTAL_REQUESTS}"
echo "  并发:      ${CONCURRENCY}"
echo "  每批:      ${BATCH_SIZE}"
echo "============================================"
echo ""

# 1. 创建 Session
echo "[1] 创建 Session..."
SESSION=$(curl -s -X POST "${BROKER_URL}/api/v1/sessions" \
    -H "Content-Type: application/json" \
    -d '{"serviceName":"LoadTestService","minimumUnits":1,"maximumUnits":50}')
SESSION_ID=$(echo "$SESSION" | python3 -c "import sys,json; print(json.load(sys.stdin)['sessionId'])")
echo "    SessionId: ${SESSION_ID}"

# 2. 构建请求 payload
build_batch() {
    local count=$1
    local items=""
    for ((i=1; i<=count; i++)); do
        [[ -n "$items" ]] && items="${items},"
        items="${items}{\"action\":\"Echo\",\"payload\":\"${PAYLOAD}\",\"userData\":\"${i}\"}"
    done
    echo "{\"requests\":[${items}]}"
}

BATCH_PAYLOAD=$(build_batch "$BATCH_SIZE")

# 3. 并发发送
echo "[2] 发送 ${TOTAL_REQUESTS} 请求 (${CONCURRENCY} 并发)..."
START_TIME=$(date +%s%N)

pids=()
for ((c=0; c<CONCURRENCY; c++)); do
    curl -s -o /dev/null -X POST "${BROKER_URL}/api/v1/sessions/${SESSION_ID}/requests" \
        -H "Content-Type: application/json" \
        -d "${BATCH_PAYLOAD}" &
    pids+=($!)
done

# 等待所有请求完成
for pid in "${pids[@]}"; do
    wait "$pid"
done

SEND_TIME=$(( ($(date +%s%N) - START_TIME) / 1000000 ))
echo "    发送耗时: ${SEND_TIME}ms"

# Flush
curl -s -o /dev/null -X POST "${BROKER_URL}/api/v1/sessions/${SESSION_ID}/requests/flush"

# 4. 等待处理并拉取响应
echo "[3] 等待处理并拉取响应..."
RECEIVED=0
WAIT_START=$(date +%s)
TIMEOUT=60

while [[ $RECEIVED -lt $TOTAL_REQUESTS ]]; do
    ELAPSED=$(( $(date +%s) - WAIT_START ))
    if [[ $ELAPSED -gt $TIMEOUT ]]; then
        echo "    ⚠ 超时 (${TIMEOUT}s)，已接收 ${RECEIVED}/${TOTAL_REQUESTS}"
        break
    fi

    RESP=$(curl -s "${BROKER_URL}/api/v1/sessions/${SESSION_ID}/responses?maxCount=500")
    BATCH_COUNT=$(echo "$RESP" | python3 -c "import sys,json; print(json.load(sys.stdin)['count'])" 2>/dev/null || echo "0")
    RECEIVED=$((RECEIVED + BATCH_COUNT))

    if [[ $BATCH_COUNT -eq 0 ]]; then
        sleep 0.5
    fi
done

END_TIME=$(date +%s%N)
TOTAL_TIME=$(( (END_TIME - START_TIME) / 1000000 ))

# 5. 关闭 Session
curl -s -o /dev/null -X DELETE "${BROKER_URL}/api/v1/sessions/${SESSION_ID}"

# 6. 输出结果
echo ""
echo "============================================"
echo "  负载测试结果"
echo "============================================"
echo "  总请求:      ${TOTAL_REQUESTS}"
echo "  已接收响应:   ${RECEIVED}"
echo "  总耗时:       ${TOTAL_TIME}ms"

if [[ $TOTAL_TIME -gt 0 ]]; then
    TPS=$(( RECEIVED * 1000 / TOTAL_TIME ))
    echo "  吞吐量:       ${TPS} req/s"
fi

if [[ $RECEIVED -lt $TOTAL_REQUESTS ]]; then
    LOSS=$(( TOTAL_REQUESTS - RECEIVED ))
    echo "  丢失:         ${LOSS} ($(( LOSS * 100 / TOTAL_REQUESTS ))%)"
fi

echo "============================================"
