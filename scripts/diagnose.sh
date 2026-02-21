#!/usr/bin/env bash
# =============================================================================
# CloudSOA Diagnostics Script
# Usage: ./scripts/diagnose.sh [namespace]
# =============================================================================
set -euo pipefail

NS="${1:-cloudsoa}"

GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

section() { echo -e "\n${GREEN}=== $* ===${NC}\n"; }

echo "============================================"
echo "  CloudSOA System Diagnostics"
echo "  Namespace: ${NS}"
echo "  Time:      $(date -u +%Y-%m-%dT%H:%M:%SZ)"
echo "============================================"

# ---- Check K8s connectivity ----
if ! kubectl cluster-info &>/dev/null 2>&1; then
    echo -e "${YELLOW}[!] Cannot connect to K8s cluster, checking local environment only${NC}\n"

    section "Local Service Status"
    if curl -s -o /dev/null -w "%{http_code}" http://localhost:5000/healthz | grep -q 200; then
        echo -e "${GREEN}Broker (localhost:5000): Healthy${NC}"
    else
        echo -e "${RED}Broker (localhost:5000): unavailable${NC}"
    fi

    section "Docker Containers"
    docker ps --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}" 2>/dev/null || echo "Docker unavailable"

    section "Redis Check"
    if docker exec cloudsoa-redis redis-cli ping 2>/dev/null | grep -q PONG; then
        echo "Redis: PONG"
        echo "Keys: $(docker exec cloudsoa-redis redis-cli DBSIZE 2>/dev/null)"
        echo ""
        echo "Session-related keys:"
        docker exec cloudsoa-redis redis-cli --scan --pattern "cloudsoa:*" 2>/dev/null | head -20
    else
        echo -e "${RED}Redis unavailable${NC}"
    fi

    exit 0
fi

# ---- K8s diagnostics ----
section "Node Status"
kubectl get nodes -o wide

section "Namespace ${NS} Overview"
kubectl -n "${NS}" get all

section "Pod Details"
kubectl -n "${NS}" get pods -o wide
echo ""
for pod in $(kubectl -n "${NS}" get pods -o name --no-headers 2>/dev/null); do
    STATUS=$(kubectl -n "${NS}" get "${pod}" -o jsonpath='{.status.phase}')
    RESTARTS=$(kubectl -n "${NS}" get "${pod}" -o jsonpath='{.status.containerStatuses[0].restartCount}' 2>/dev/null || echo "?")
    if [[ "$STATUS" != "Running" || "$RESTARTS" -gt 5 ]]; then
        echo -e "${RED}${pod}: ${STATUS} (restarts: ${RESTARTS})${NC}"
        echo "  Recent logs:"
        kubectl -n "${NS}" logs "${pod}" --tail=5 2>/dev/null | sed 's/^/    /'
    fi
done

section "Service Endpoints"
kubectl -n "${NS}" get endpoints

section "HPA Status"
kubectl -n "${NS}" get hpa 2>/dev/null || echo "No HPA"

section "KEDA ScaledObjects"
kubectl -n "${NS}" get scaledobject 2>/dev/null || echo "No KEDA ScaledObject"

section "Recent Events (Warning)"
kubectl -n "${NS}" get events --field-selector type=Warning --sort-by='.lastTimestamp' 2>/dev/null | tail -10 || echo "No warning events"

section "Broker Logs (last 20 lines)"
kubectl -n "${NS}" logs -l app=broker --tail=20 2>/dev/null || echo "No Broker Pod"

section "Resource Usage"
kubectl -n "${NS}" top pods 2>/dev/null || echo "Metrics server not installed"

echo ""
echo "============================================"
echo "  Diagnostics complete"
echo "============================================"
