#!/usr/bin/env python3
"""Comprehensive tunnel testing script"""
import subprocess
import sys
import time
import statistics

def test_dns_resolution(domains):
    """Test DNS resolution for all domains"""
    print("=" * 60)
    print("DNS Resolution Test")
    print("=" * 60)
    
    results = {}
    for domain in domains:
        try:
            result = subprocess.run(
                ["nslookup", f"{domain}.denys.fast", "8.8.8.8"],
                capture_output=True,
                text=True,
                timeout=5
            )
            if "159.223.76.215" in result.stdout:
                results[domain] = "OK"
                print(f"OK: {domain}.denys.fast -> 159.223.76.215")
            else:
                results[domain] = "FAIL"
                print(f"FAIL: {domain}.denys.fast -> NOT RESOLVED")
        except Exception as e:
            results[domain] = "ERROR"
            print(f"ERROR: {domain}.denys.fast -> {e}")
    
    return results

def test_https_availability(domains):
    """Test HTTPS availability and response time"""
    print("\n" + "=" * 60)
    print("HTTPS Availability & Response Time Test")
    print("=" * 60)
    
    results = {}
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
            elapsed = (time.time() - start) * 1000  # ms
            
            if result.returncode == 0 and result.stdout.strip() in ["200", "301", "302", "307", "308"]:
                status = result.stdout.strip()
                results[domain] = {"status": "OK", "code": status, "time_ms": elapsed}
                print(f"OK: {domain}.denys.fast -> HTTP {status} ({elapsed:.2f}ms)")
            else:
                results[domain] = {"status": "FAIL", "code": result.stdout.strip(), "time_ms": None}
                print(f"FAIL: {domain}.denys.fast -> FAILED (code: {result.stdout.strip()})")
        except Exception as e:
            results[domain] = {"status": "ERROR", "code": None, "time_ms": None}
            print(f"ERROR: {domain}.denys.fast -> {e}")
    
    return results

def test_packet_loss(host, count=100):
    """Test packet loss to host"""
    print("\n" + "=" * 60)
    print(f"Packet Loss Test (ping {host} {count} times)")
    print("=" * 60)
    
    try:
        result = subprocess.run(
            ["ping", "-c", str(count), host],
            capture_output=True,
            text=True,
            timeout=120
        )
        
        # Parse ping output
        output = result.stdout
        if "packet loss" in output:
            loss_line = [l for l in output.split('\n') if 'packet loss' in l][0]
            loss_pct = loss_line.split('%')[0].split()[-1]
            
            # Extract min/avg/max
            stats_line = [l for l in output.split('\n') if 'min/avg/max' in l]
            if stats_line:
                stats = stats_line[0].split('=')[1].strip().split('/')
                min_time, avg_time, max_time = stats[0], stats[1], stats[2]
                print(f"Packet Loss: {loss_pct}%")
                print(f"RTT: min={min_time}ms, avg={avg_time}ms, max={max_time}ms")
                return {
                    "loss": float(loss_pct),
                    "min": float(min_time),
                    "avg": float(avg_time),
                    "max": float(max_time)
                }
        print("Could not parse ping output")
        return None
    except Exception as e:
        print(f"ERROR: {e}")
        return None

def test_connection_stability(host, duration=60, interval=1):
    """Test connection stability over time"""
    print("\n" + "=" * 60)
    print(f"Connection Stability Test ({duration} seconds)")
    print("=" * 60)
    
    results = []
    start_time = time.time()
    end_time = start_time + duration
    
    print("Testing...", end="", flush=True)
    while time.time() < end_time:
        try:
            result = subprocess.run(
                ["ping", "-c", "1", "-W", "1", host],
                capture_output=True,
                text=True,
                timeout=3
            )
            if result.returncode == 0:
                results.append(1)  # Success
                print(".", end="", flush=True)
            else:
                results.append(0)  # Failure
                print("X", end="", flush=True)
        except:
            results.append(0)
            print("X", end="", flush=True)
        
        time.sleep(interval)
    
    print()  # New line
    success_rate = (sum(results) / len(results)) * 100 if results else 0
    print(f"Success Rate: {success_rate:.2f}% ({sum(results)}/{len(results)})")
    return success_rate

def test_speed(domains, iterations=10):
    """Test response speed for multiple iterations"""
    print("\n" + "=" * 60)
    print(f"Speed Test ({iterations} iterations per domain)")
    print("=" * 60)
    
    all_times = {}
    for domain in domains:
        url = f"https://{domain}.denys.fast"
        times = []
        
        print(f"\n{domain}.denys.fast:")
        for i in range(iterations):
            try:
                start = time.time()
                result = subprocess.run(
                    ["curl", "-s", "-o", "/dev/null", "-w", "%{http_code}", "--max-time", "10", url],
                    capture_output=True,
                    text=True,
                    timeout=15
                )
                elapsed = (time.time() - start) * 1000
                
                if result.returncode == 0 and result.stdout.strip() in ["200", "301", "302"]:
                    times.append(elapsed)
                    print(f"  Iteration {i+1}: {elapsed:.2f}ms", end="")
                    if i < iterations - 1:
                        print()
                else:
                    print(f"  Iteration {i+1}: FAILED")
            except Exception as e:
                print(f"  Iteration {i+1}: ERROR - {e}")
        
        if times:
            avg = statistics.mean(times)
            median = statistics.median(times)
            min_time = min(times)
            max_time = max(times)
            all_times[domain] = {
                "avg": avg,
                "median": median,
                "min": min_time,
                "max": max_time
            }
            print(f"\n  Average: {avg:.2f}ms, Median: {median:.2f}ms, Min: {min_time:.2f}ms, Max: {max_time:.2f}ms")
    
    return all_times

if __name__ == "__main__":
    domains = ["n8n", "gpu", "apigencomf", "simuqlator", "tg", "llm", "interfaceblob", "server1", "server1gpu"]
    
    print("WireGuard Tunnel Comprehensive Test")
    print("=" * 60)
    
    # DNS Test
    dns_results = test_dns_resolution(domains)
    
    # HTTPS Availability
    https_results = test_https_availability(domains)
    
    # Packet Loss Test (to DO VM)
    packet_loss = test_packet_loss("159.223.76.215", count=50)
    
    # Connection Stability
    stability = test_connection_stability("159.223.76.215", duration=30, interval=1)
    
    # Speed Test
    speed_results = test_speed(domains[:3], iterations=5)  # Test first 3 domains
    
    # Summary
    print("\n" + "=" * 60)
    print("SUMMARY")
    print("=" * 60)
    print(f"DNS Resolution: {sum(1 for v in dns_results.values() if v == 'OK')}/{len(dns_results)} OK")
    print(f"HTTPS Availability: {sum(1 for v in https_results.values() if v['status'] == 'OK')}/{len(https_results)} OK")
    if packet_loss:
        print(f"Packet Loss: {packet_loss['loss']}%")
        print(f"Average RTT: {packet_loss['avg']}ms")
    print(f"Connection Stability: {stability:.2f}%")
    if speed_results:
        avg_speed = statistics.mean([v['avg'] for v in speed_results.values()])
        print(f"Average Response Time: {avg_speed:.2f}ms")
