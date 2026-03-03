#!/bin/bash
# Comprehensive tunnel test script

echo "============================================================"
echo "WireGuard Tunnel Comprehensive Test"
echo "============================================================"

DOMAINS=("n8n" "gpu" "apigencomf" "simuqlator" "tg" "llm" "interfaceblob" "server1" "server1gpu")

echo ""
echo "=== HTTPS Availability & Response Time ==="
for domain in "${DOMAINS[@]}"; do
    url="https://${domain}.denys.fast"
    result=$(curl -s -o /dev/null -w '%{http_code}|%{time_total}' --max-time 10 "$url" 2>&1)
    code=$(echo "$result" | cut -d'|' -f1)
    time=$(echo "$result" | cut -d'|' -f2)
    
    if [ "$code" = "200" ] || [ "$code" = "301" ] || [ "$code" = "302" ]; then
        echo "OK: $domain.denys.fast -> HTTP $code (${time}s)"
    else
        echo "FAIL: $domain.denys.fast -> HTTP $code"
    fi
done

echo ""
echo "=== Packet Loss Test (WireGuard tunnel) ==="
ping -c 50 10.0.0.2 | tail -2

echo ""
echo "=== Packet Loss Test (Internal IP) ==="
ping -c 50 192.168.66.142 | tail -2

echo ""
echo "=== Speed Test (10 iterations) ==="
echo "Testing gpu.denys.fast:"
total=0
count=0
for i in {1..10}; do
    time=$(curl -s -o /dev/null -w '%{time_total}' --max-time 10 https://gpu.denys.fast 2>&1)
    if [ $? -eq 0 ]; then
        echo "  Iteration $i: ${time}s"
        total=$(echo "$total + $time" | bc)
        count=$((count + 1))
    else
        echo "  Iteration $i: FAILED"
    fi
done
if [ $count -gt 0 ]; then
    avg=$(echo "scale=3; $total / $count" | bc)
    echo "  Average: ${avg}s"
fi

echo ""
echo "=== Connection Stability Test (30 seconds) ==="
success=0
total=30
for i in $(seq 1 $total); do
    if ping -c 1 -W 1 10.0.0.2 > /dev/null 2>&1; then
        success=$((success + 1))
        echo -n "."
    else
        echo -n "X"
    fi
    sleep 1
done
echo ""
stability=$(echo "scale=2; $success * 100 / $total" | bc)
echo "Success Rate: ${stability}% ($success/$total)"

echo ""
echo "=== WireGuard Status ==="
wg show

echo ""
echo "=== Nginx Status ==="
systemctl status nginx --no-pager -l | head -5

echo ""
echo "============================================================"
echo "Test Complete"
echo "============================================================"
