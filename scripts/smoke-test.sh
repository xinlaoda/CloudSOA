#!/usr/bin/env bash
# =============================================================================
# CloudSOA 冒烟测试脚本
# 用法: ./scripts/smoke-test.sh [BROKER_URL]
# =============================================================================
set -euo pipefail

GREEN='\033[0;32m'
RED='\033[0;31m'
NC='\033[0m'

BROKER_URL="${1:-http://localhost:5000}"
PASSED=0
FAILED=0

pass() { echo -e "  ${GREEN}✓${NC} $*"; PASSED=$((PASSED+1)); }
fail() { echo -e "  ${RED}✗${NC} $*"; FAILED=$((FAILED+1)); }

echo "============================================"
echo "  CloudSOA 冒烟测试"
echo "  Broker: ${BROKER_URL}"
echo "============================================"
echo ""

# ---- Test 1: 健康检查 ----
echo "[1/8] 健康检查"
RESP=$(curl -s -w "\n%{http_code}" "${BROKER_URL}/healthz")
CODE=$(echo "$RESP" | tail -1)
BODY=$(echo "$RESP" | head -1)
if [[ "$CODE" == "200" && "$BODY" == "Healthy" ]]; then
    pass "GET /healthz → 200 Healthy"
else
    fail "GET /healthz → ${CODE} ${BODY}"
fi

# ---- Test 2: Metrics ----
echo "[2/8] 指标端点"
CODE=$(curl -s -o /dev/null -w "%{http_code}" "${BROKER_URL}/metrics")
if [[ "$CODE" == "200" ]]; then
    pass "GET /metrics → 200"
else
    fail "GET /metrics → ${CODE}"
fi

# ---- Test 3: 创建 Session ----
echo "[3/8] 创建 Session"
RESP=$(curl -s -w "\n%{http_code}" -X POST "${BROKER_URL}/api/v1/sessions" \
    -H "Content-Type: application/json" \
    -d '{"serviceName":"SmokeTestService","minimumUnits":1,"maximumUnits":5}')
CODE=$(echo "$RESP" | tail -1)
BODY=$(echo "$RESP" | head -1)

if [[ "$CODE" == "201" ]]; then
    SESSION_ID=$(echo "$BODY" | python3 -c "import sys,json; print(json.load(sys.stdin)['sessionId'])" 2>/dev/null || echo "")
    if [[ -n "$SESSION_ID" ]]; then
        pass "POST /sessions → 201, sessionId=${SESSION_ID:0:12}..."
    else
        fail "POST /sessions → 201 但无法解析 sessionId"
    fi
else
    fail "POST /sessions → ${CODE}"
    SESSION_ID=""
fi

if [[ -z "$SESSION_ID" ]]; then
    echo ""
    echo "  无法继续测试（Session 创建失败）"
    echo ""
    echo "结果: ${PASSED} 通过, ${FAILED} 失败"
    exit 1
fi

# ---- Test 4: 查询 Session ----
echo "[4/8] 查询 Session"
CODE=$(curl -s -o /dev/null -w "%{http_code}" "${BROKER_URL}/api/v1/sessions/${SESSION_ID}")
if [[ "$CODE" == "200" ]]; then
    pass "GET /sessions/{id} → 200"
else
    fail "GET /sessions/{id} → ${CODE}"
fi

# ---- Test 5: 发送请求 ----
echo "[5/8] 发送请求 (3条)"
PAYLOAD=$(echo -n "smoke-test-data" | base64)
RESP=$(curl -s -w "\n%{http_code}" -X POST "${BROKER_URL}/api/v1/sessions/${SESSION_ID}/requests" \
    -H "Content-Type: application/json" \
    -d "{\"requests\":[
        {\"action\":\"Echo\",\"payload\":\"${PAYLOAD}\",\"userData\":\"1\"},
        {\"action\":\"Echo\",\"payload\":\"${PAYLOAD}\",\"userData\":\"2\"},
        {\"action\":\"Echo\",\"payload\":\"${PAYLOAD}\",\"userData\":\"3\"}
    ]}")
CODE=$(echo "$RESP" | tail -1)

if [[ "$CODE" == "202" ]]; then
    pass "POST /requests → 202 (3 enqueued)"
else
    fail "POST /requests → ${CODE}"
fi

# ---- Test 6: 等待并拉取响应 ----
echo "[6/8] 拉取响应 (等待3秒)"
sleep 3
RESP=$(curl -s -w "\n%{http_code}" "${BROKER_URL}/api/v1/sessions/${SESSION_ID}/responses?maxCount=10")
CODE=$(echo "$RESP" | tail -1)
BODY=$(echo "$RESP" | head -1)

if [[ "$CODE" == "200" ]]; then
    COUNT=$(echo "$BODY" | python3 -c "import sys,json; print(json.load(sys.stdin)['count'])" 2>/dev/null || echo "0")
    if [[ "$COUNT" -ge 3 ]]; then
        pass "GET /responses → 200, count=${COUNT}"
    else
        fail "GET /responses → 200, 但 count=${COUNT} (期望>=3)"
    fi
else
    fail "GET /responses → ${CODE}"
fi

# ---- Test 7: 关闭 Session ----
echo "[7/8] 关闭 Session"
CODE=$(curl -s -o /dev/null -w "%{http_code}" -X DELETE "${BROKER_URL}/api/v1/sessions/${SESSION_ID}")
if [[ "$CODE" == "204" ]]; then
    pass "DELETE /sessions/{id} → 204"
else
    fail "DELETE /sessions/{id} → ${CODE}"
fi

# ---- Test 8: 404 测试 ----
echo "[8/8] 查询不存在的 Session"
CODE=$(curl -s -o /dev/null -w "%{http_code}" "${BROKER_URL}/api/v1/sessions/nonexistent-session-id")
if [[ "$CODE" == "404" ]]; then
    pass "GET /sessions/invalid → 404"
else
    fail "GET /sessions/invalid → ${CODE}"
fi

# ---- 结果 ----
echo ""
echo "============================================"
if [[ $FAILED -eq 0 ]]; then
    echo -e "  ${GREEN}✅ 全部通过: ${PASSED}/${PASSED}${NC}"
else
    echo -e "  ${RED}❌ 失败: ${FAILED}, 通过: ${PASSED}${NC}"
fi
echo "============================================"

exit $FAILED
