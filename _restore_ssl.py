#!/usr/bin/env python3
"""Restore SSL blocks for all domains"""
import sys
import subprocess

try:
    import paramiko
except ImportError:
    subprocess.check_call([sys.executable, "-m", "pip", "install", "paramiko", "--quiet"])
    import paramiko

def restore_ssl(host, user, password):
    """Restore SSL for all domains"""
    client = paramiko.SSHClient()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    
    try:
        client.connect(host, username=user, password=password, timeout=10)
        
        domains = ["n8n", "gpu", "apigencomf", "simuqlator", "tg", "llm", "interfaceblob", "server1", "server1gpu", "comfy1", "vllm"]
        
        print("Restoring SSL certificates for all domains...")
        
        for domain in domains:
            full_domain = f"{domain}.denys.fast"
            print(f"\nProcessing {full_domain}...")
            
            # Check if HTTPS block exists
            stdin, stdout, stderr = client.exec_command(
                f"grep -q 'listen 443' /etc/nginx/sites-available/{full_domain} && echo 'EXISTS' || echo 'MISSING'"
            )
            has_https = stdout.read().decode('utf-8').strip() == 'EXISTS'
            
            if not has_https:
                print(f"  SSL block missing, restoring...")
                stdin, stdout, stderr = client.exec_command(
                    f"certbot --nginx -d {full_domain} --non-interactive --agree-tos --email admin@denys.fast 2>&1 | tail -3"
                )
                output = stdout.read().decode('utf-8')
                error = stderr.read().decode('utf-8')
                if "successfully enabled HTTPS" in output or "Certificate not yet due" in output:
                    print(f"  OK: SSL restored for {full_domain}")
                else:
                    print(f"  WARNING: {output}")
            else:
                print(f"  OK: SSL already configured")
        
        # Test and reload
        print("\nTesting Nginx configuration...")
        stdin, stdout, stderr = client.exec_command("nginx -t")
        exit_status = stdout.channel.recv_exit_status()
        
        if exit_status == 0:
            print("OK: Nginx configuration is valid")
            stdin, stdout, stderr = client.exec_command("systemctl reload nginx")
            stdout.channel.recv_exit_status()
            print("OK: Nginx reloaded")
        else:
            error = stderr.read().decode('utf-8')
            print(f"ERROR: {error}")
            return 1
        
        client.close()
        return 0
        
    except Exception as e:
        print(f"Error: {e}", file=sys.stderr)
        import traceback
        traceback.print_exc()
        return 1

if __name__ == "__main__":
    if len(sys.argv) < 4:
        print("Usage: python _restore_ssl.py <host> <user> <password>", file=sys.stderr)
        sys.exit(1)
    
    exit_code = restore_ssl(sys.argv[1], sys.argv[2], sys.argv[3])
    sys.exit(exit_code)
