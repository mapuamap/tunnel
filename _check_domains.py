#!/usr/bin/env python3
"""Check all domains status"""
import subprocess
import sys

try:
    import paramiko
except ImportError:
    subprocess.check_call([sys.executable, "-m", "pip", "install", "paramiko", "--quiet"])
    import paramiko

def check_domains(host, user, password):
    """Check all domains"""
    client = paramiko.SSHClient()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    
    try:
        client.connect(host, username=user, password=password, timeout=10)
        
        domains = ["server1", "interfaceblob", "llm", "simuqlator", "gpu", "n8n"]
        
        print("=" * 60)
        print("Domain Status Check")
        print("=" * 60)
        
        results = {}
        
        # Check HTTPS
        print("\n=== HTTPS Availability ===")
        for domain in domains:
            url = f"https://{domain}.denys.fast"
            cmd = f"curl -s -o /dev/null -w 'HTTP:%{{http_code}}|Time:%{{time_total}}s|SSL:%{{ssl_verify_result}}' --max-time 10 {url}"
            
            stdin, stdout, stderr = client.exec_command(cmd)
            output = stdout.read().decode('utf-8').strip()
            error = stderr.read().decode('utf-8')
            
            if output:
                parts = output.split('|')
                http_code = parts[0].split(':')[1] if ':' in parts[0] else 'N/A'
                time = parts[1].split(':')[1] if len(parts) > 1 and ':' in parts[1] else 'N/A'
                ssl = parts[2].split(':')[1] if len(parts) > 2 and ':' in parts[2] else 'N/A'
                
                status = "OK" if http_code in ["200", "301", "302", "307", "308"] else "FAIL"
                results[domain] = {"status": status, "code": http_code, "time": time, "ssl": ssl}
                
                print(f"{domain}.denys.fast:25 -> HTTP {http_code:3} | {time:6} | SSL:{ssl} | {status}")
            else:
                results[domain] = {"status": "ERROR", "code": "N/A"}
                print(f"{domain}.denys.fast:25 -> ERROR")
        
        # Check upstream services
        print("\n=== Upstream Services ===")
        upstreams = {
            "server1": "192.168.66.141:8006",
            "interfaceblob": "192.168.66.154:6001",
            "llm": "192.168.66.154:3000",
            "simuqlator": "192.168.66.154:80",
            "gpu": "192.168.66.142:5000",
            "n8n": "192.168.66.150:5678"
        }
        
        for domain, upstream in upstreams.items():
            cmd = f"curl -s -o /dev/null -w '%{{http_code}}' --max-time 5 http://{upstream}"
            stdin, stdout, stderr = client.exec_command(cmd)
            code = stdout.read().decode('utf-8').strip()
            status = "OK" if code in ["200", "301", "302", "401", "403"] else "FAIL"
            print(f"{domain:15} -> {upstream:20} | HTTP {code:3} | {status}")
        
        # Check Nginx configs
        print("\n=== Nginx Configs ===")
        for domain in domains:
            cmd = f"test -f /etc/nginx/sites-available/{domain}.denys.fast && echo 'EXISTS' || echo 'MISSING'"
            stdin, stdout, stderr = client.exec_command(cmd)
            exists = stdout.read().decode('utf-8').strip()
            print(f"{domain}.denys.fast:25 -> {exists}")
        
        # Summary
        print("\n" + "=" * 60)
        print("SUMMARY")
        print("=" * 60)
        ok_count = sum(1 for v in results.values() if v.get("status") == "OK")
        print(f"Working domains: {ok_count}/{len(domains)}")
        
        for domain, result in results.items():
            if result.get("status") != "OK":
                print(f"  - {domain}.denys.fast: {result.get('code', 'N/A')}")
        
        client.close()
        return results
        
    except Exception as e:
        print(f"Error: {e}", file=sys.stderr)
        return None

if __name__ == "__main__":
    if len(sys.argv) < 4:
        print("Usage: python _check_domains.py <host> <user> <password>", file=sys.stderr)
        sys.exit(1)
    
    check_domains(sys.argv[1], sys.argv[2], sys.argv[3])
