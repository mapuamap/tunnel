#!/usr/bin/env python3
"""Comprehensive tunnel test - run on server"""
import subprocess
import time
import statistics

domains = ["n8n", "gpu", "apigencomf", "simuqlator", "tg", "llm", "interfaceblob", "server1", "server1gpu"]

print("=" * 60)
print("WireGuard Tunnel Comprehensive Test")
print("=" * 60)

# HTTPS Availability
print("\n=== HTTPS Availability & Response Time ===")
https_results = {}
for domain in domains:
    url = f"https://{domain}.denys.fast"
    try:
        start = time.time()
        result = subprocess.run(
            ["curl", "-s", "-o", "/dev/null", "-w", "%{http_code}", "--max-time", "10", url],
            capture_output=True,
            text=True,
            timeout=15
        )
        elapsed = time.time() - start
        code = result.stdout.strip()
        
        if code in ["200", "301", "302", "307", "308"]:
            https_results[domain] = {"status": "OK", "code": code, "time": elapsed}
            print(f"OK: {domain}.denys.fast -> HTTP {code} ({elapsed:.3f}s)")
        else:
            https_results[domain] = {"status": "FAIL", "code": code}
            print(f"FAIL: {domain}.denys.fast -> HTTP {code}")
    except Exception as e:
        https_results[domain] = {"status": "ERROR", "error": str(e)}
        print(f"ERROR: {domain}.denys.fast -> {e}")

# Packet Loss Test - WireGuard tunnel
print("\n=== Packet Loss Test (50 pings to LXC via WireGuard) ===")
try:
    result = subprocess.run(
        ["ping", "-c", "50", "10.0.0.2"],
        capture_output=True,
        text=True,
        timeout=60
    )
    output = result.stdout
    if "packet loss" in output:
        loss_line = [l for l in output.split('\n') if 'packet loss' in l][0]
        stats_line = [l for l in output.split('\n') if 'min/avg/max' in l]
        print(loss_line)
        if stats_line:
            print(stats_line[0])
except Exception as e:
    print(f"ERROR: {e}")

# Packet Loss Test - Internal IP
print("\n=== Packet Loss Test (50 pings to internal IP) ===")
try:
    result = subprocess.run(
        ["ping", "-c", "50", "192.168.66.142"],
        capture_output=True,
        text=True,
        timeout=60
    )
    output = result.stdout
    if "packet loss" in output:
        loss_line = [l for l in output.split('\n') if 'packet loss' in l][0]
        stats_line = [l for l in output.split('\n') if 'min/avg/max' in l]
        print(loss_line)
        if stats_line:
            print(stats_line[0])
except Exception as e:
    print(f"ERROR: {e}")

# Speed Test
print("\n=== Speed Test (10 iterations for gpu.denys.fast) ===")
times = []
for i in range(1, 11):
    try:
        start = time.time()
        result = subprocess.run(
            ["curl", "-s", "-o", "/dev/null", "-w", "%{http_code}", "--max-time", "10", "https://gpu.denys.fast"],
            capture_output=True,
            text=True,
            timeout=15
        )
        elapsed = time.time() - start
        if result.returncode == 0 and result.stdout.strip() in ["200", "301", "302"]:
            times.append(elapsed)
            print(f"  Iteration {i}: {elapsed:.3f}s")
        else:
            print(f"  Iteration {i}: FAILED")
    except Exception as e:
        print(f"  Iteration {i}: ERROR - {e}")

if times:
    avg = statistics.mean(times)
    median = statistics.median(times)
    min_time = min(times)
    max_time = max(times)
    print(f"  Average: {avg:.3f}s, Median: {median:.3f}s, Min: {min_time:.3f}s, Max: {max_time:.3f}s")

# Connection Stability
print("\n=== Connection Stability Test (30 seconds) ===")
success = 0
total = 30
for i in range(1, total + 1):
    try:
        result = subprocess.run(
            ["ping", "-c", "1", "-W", "1", "10.0.0.2"],
            capture_output=True,
            timeout=3
        )
        if result.returncode == 0:
            success += 1
            print(".", end="", flush=True)
        else:
            print("X", end="", flush=True)
    except:
        print("X", end="", flush=True)
    time.sleep(1)

print()
stability = (success / total) * 100
print(f"Success Rate: {stability:.2f}% ({success}/{total})")

# WireGuard Status
print("\n=== WireGuard Status ===")
try:
    result = subprocess.run(["wg", "show"], capture_output=True, text=True, timeout=5)
    print(result.stdout)
except Exception as e:
    print(f"ERROR: {e}")

# Summary
print("\n" + "=" * 60)
print("SUMMARY")
print("=" * 60)
ok_count = sum(1 for v in https_results.values() if v.get("status") == "OK")
print(f"HTTPS Availability: {ok_count}/{len(domains)} OK")
if times:
    print(f"Average Response Time: {statistics.mean(times):.3f}s")
print(f"Connection Stability: {stability:.2f}%")
print("=" * 60)
