#!/usr/bin/env python3
"""Fix all Nginx configs - remove proxy_cookie_flags and ensure proper headers"""
import sys
import subprocess

try:
    import paramiko
except ImportError:
    subprocess.check_call([sys.executable, "-m", "pip", "install", "paramiko", "--quiet"])
    import paramiko

def fix_all_configs(host, user, password):
    """Fix all Nginx configs"""
    client = paramiko.SSHClient()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    
    try:
        client.connect(host, username=user, password=password, timeout=10)
        
        # Remove proxy_cookie_flags from all configs
        stdin, stdout, stderr = client.exec_command(
            "find /etc/nginx/sites-available -name '*.denys.fast' -exec sed -i '/proxy_cookie_flags/d' {} \\; && echo 'Removed proxy_cookie_flags'"
        )
        stdout.channel.recv_exit_status()
        print("Removed proxy_cookie_flags from all configs")
        
        # Fix apigencomf - recreate it properly
        config = """server {
    server_name apigencomf.denys.fast;
    
    location / {
        proxy_pass http://192.168.66.142:8188;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto https;
        proxy_set_header X-Forwarded-Scheme https;
        proxy_set_header X-Forwarded-Host $host;
        proxy_set_header X-Forwarded-Port 443;
        proxy_cookie_path / /;
        proxy_connect_timeout 60s;
        proxy_send_timeout 60s;
        proxy_read_timeout 60s;
        proxy_buffering off;
        proxy_request_buffering off;
    }

    listen 443 ssl;
    ssl_certificate /etc/letsencrypt/live/apigencomf.denys.fast/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/apigencomf.denys.fast/privkey.pem;
    include /etc/letsencrypt/options-ssl-nginx.conf;
    ssl_dhparam /etc/letsencrypt/ssl-dhparams.pem;
}

server {
    if ($host = apigencomf.denys.fast) {
        return 301 https://$host$request_uri;
    }
    listen 80;
    server_name apigencomf.denys.fast;
    return 404;
}
"""
        
        sftp = client.open_sftp()
        with sftp.open('/etc/nginx/sites-available/apigencomf.denys.fast', 'w') as f:
            f.write(config)
        sftp.close()
        
        print("Fixed apigencomf.denys.fast")
        
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
        print("Usage: python _fix_all_nginx.py <host> <user> <password>", file=sys.stderr)
        sys.exit(1)
    
    exit_code = fix_all_configs(sys.argv[1], sys.argv[2], sys.argv[3])
    sys.exit(exit_code)
