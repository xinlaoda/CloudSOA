#!/usr/bin/env bash
# =============================================================================
# CloudSOA 诊断脚本
# 用法: ./scripts/diagnose.sh [namespace]
# =============================================================================
set -euo pipefail

NS="${1:-cloudsoa}"

GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

section() { echo -e "\n${GREEN}=== $* ===${NC}\n"; }

echo "============================================"
echo "  CloudSOA 系统诊断"
echo "  命名空间: ${NS}"
echo "  时间:     $(date -u +%Y-%m-%dT%H:%M:%SZ)"
echo "============================================"

# ---- 检查是否可连接 K8s ----
if ! kubectl cluster-info &>/dev/null 2>&1; then
    echo -e "${YELLOW}[!] 无法连接 K8s 集群，仅检查本地环境${NC}\n"

    section "本地服务状态"
    if curl -s -o /dev/null -w "%{http_code}" http://localhost:5000/healthz | grep -q 200; then
        echo -e "${GREEN}Broker (localhost:5000): Healthy${NC}"
    else
        echo -e "${RED}Broker (localhost:5000): 不可用${NC}"
    fi

    section "Docker 容器"
    docker ps --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}" 2>/dev/null || echo "Docker 不可用"

    section "Redis 检查"
    if docker exec cloudsoa-redis redis-cli ping 2>/dev/null | grep -q PONG; then
        echo "Redis: PONG"
        echo "Keys: $(docker exec cloudsoa-redis redis-cli DBSIZE 2>/dev/null)"
        echo ""
        echo "Session 相关 keys:"
        docker exec cloudsoa-redis redis-cli --scan --pattern "cloudsoa:*" 2>/dev/null | head -20
    else
        echo -e "${RED}Redis 不可用${NC}"
    fi

    exit 0
fi

# ---- K8s 诊断 ----
section "节点状态"
kubectl get nodes -o wide

section "命名空间 ${NS} 概览"
kubectl -n "${NS}" get all

section "Pod 详情"
kubectl -n "${NS}" get pods -o wide
echo ""
for pod in $(kubectl -n "${NS}" get pods -o name --no-headers 2>/dev/null); do
    STATUS=$(kubectl -n "${NS}" get "${pod}" -o jsonpath='{.status.phase}')
    RESTARTS=$(kubectl -n "${NS}" get "${pod}" -o jsonpath='{.status.containerStatuses[0].restartCount}' 2>/dev/null || echo "?")
    if [[ "$STATUS" != "Running" || "$RESTARTS" -gt 5 ]]; then
        echo -e "${RED}${pod}: ${STATUS} (restarts: ${RESTARTS})${NC}"
        echo "  最近日志:"
        kubectl -n "${NS}" logs "${pod}" --tail=5 2>/dev/null | sed 's/^/    /'
    fi
done

section "Service 端点"
kubectl -n "${NS}" get endpoints

section "HPA 状态"
kubectl -n "${NS}" get hpa 2>/dev/null || echo "无 HPA"

section "KEDA ScaledObjects"
kubectl -n "${NS}" get scaledobject 2>/dev/null || echo "无 KEDA ScaledObject"

section "最近事件 (Warning)"
kubectl -n "${NS}" get events --field-selector type=Warning --sort-by='.lastTimestamp' 2>/dev/null | tail -10 || echo "无警告事件"

section "Broker 日志 (最近20行)"
kubectl -n "${NS}" logs -l app=broker --tail=20 2>/dev/null || echo "无 Broker Pod"

section "资源使用"
kubectl -n "${NS}" top pods 2>/dev/null || echo "Metrics server 未安装"

echo ""
echo "============================================"
echo "  诊断完成"
echo "============================================"
