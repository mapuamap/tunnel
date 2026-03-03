#!/usr/bin/env python3
"""Fix server1.denys.fast configuration to handle Proxmox redirects"""
import sys
import subprocess

try:
    import paramiko
except ImportError:
    subprocess.check_call([sys.executable, "-m", "pip", "install", "paramiko", "--quiet"])
    import paramiko

def fix_server1(host, user, password):
    """Fix server1 configuration"""
    client = paramiko.SSHClient()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    
    try:
        client.connect(host, username=user, password=password, timeout=10)
        
        config = """server {
    server_name server1.denys.fast;
    
    location / {
        # Proxmox requires HTTPS - proxy directly to HTTPS to avoid redirect loops
        proxy_pass https://192.168.66.141:8006;
        
        # SSL verification for upstream (Proxmox uses self-signed cert)
        proxy_ssl_verify off;
        proxy_ssl_server_name on;
        
        # Standard headers
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        
        # CRITICAL: Force HTTPS for cookies
        proxy_set_header X-Forwarded-Proto https;
        proxy_set_header X-Forwarded-Scheme https;
        proxy_set_header X-Forwarded-Host $host;
        proxy_set_header X-Forwarded-Port 443;
        
        # Cookie handling
        proxy_cookie_path / /;
        
        # Handle Proxmox redirects - rewrite Location header in response
        proxy_redirect https://192.168.66.141:8006/ https://server1.denys.fast/;
        proxy_redirect http://192.168.66.141:8006/ https://server1.denys.fast/;
        
        # Also rewrite URLs in HTML body (Proxmox may embed absolute URLs)
        sub_filter 'https://192.168.66.141:8006' 'https://server1.denys.fast';
        sub_filter 'http://192.168.66.141:8006' 'https://server1.denys.fast';
        sub_filter_once off;
        sub_filter_types text/html text/css text/javascript application/javascript;
        
        # Timeouts
        proxy_connect_timeout 60s;
        proxy_send_timeout 60s;
        proxy_read_timeout 60s;
        
        # Buffer settings
        proxy_buffering off;
        proxy_request_buffering off;
    }

    listen 443 ssl;
    ssl_certificate /etc/letsencrypt/live/server1.denys.fast-0001/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/server1.denys.fast-0001/privkey.pem;
    include /etc/letsencrypt/options-ssl-nginx.conf;
    ssl_dhparam /etc/letsencrypt/ssl-dhparams.pem;
}

server {
    if ($host = server1.denys.fast) {
        return 301 https://$host$request_uri;
    }
    listen 80;
    server_name server1.denys.fast;
    return 404;
}
"""
        
        sftp = client.open_sftp()
        with sftp.open('/etc/nginx/sites-available/server1.denys.fast', 'w') as f:
            f.write(config)
        sftp.close()
        
        # Test and reload
        stdin, stdout, stderr = client.exec_command("nginx -t")
        exit_status = stdout.channel.recv_exit_status()
        error = stderr.read().decode('utf-8')
        
        if exit_status == 0:
            print("OK: Nginx config is valid")
            stdin, stdout, stderr = client.exec_command("systemctl reload nginx")
            stdout.channel.recv_exit_status()
            print("OK: Nginx reloaded")
            return 0
        else:
            print(f"ERROR: {error}")
            return 1
        
        client.close()
        
    except Exception as e:
        print(f"Error: {e}", file=sys.stderr)
        import traceback
        traceback.print_exc()
        return 1

if __name__ == "__main__":
    if len(sys.argv) < 4:
        print("Usage: python _fix_server1.py <host> <user> <password>", file=sys.stderr)
        sys.exit(1)
    
    exit_code = fix_server1(sys.argv[1], sys.argv[2], sys.argv[3])
    sys.exit(exit_code)
