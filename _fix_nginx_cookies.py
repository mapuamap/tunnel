#!/usr/bin/env python3
"""Fix Nginx configurations for proper cookie handling through reverse proxy"""
import sys
import subprocess
import re

try:
    import paramiko
except ImportError:
    subprocess.check_call([sys.executable, "-m", "pip", "install", "paramiko", "--quiet"])
    import paramiko

def fix_nginx_config(host, user, password):
    """Fix all Nginx configs for cookie handling"""
    client = paramiko.SSHClient()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    
    try:
        client.connect(host, username=user, password=password, timeout=10)
        sftp = client.open_sftp()
        
        # Get all domain configs
        stdin, stdout, stderr = client.exec_command("ls /etc/nginx/sites-available/*.denys.fast")
        config_files = [f.strip() for f in stdout.read().decode('utf-8').split('\n') if f.strip()]
        
        print(f"Found {len(config_files)} config files to update")
        
        for config_path in config_files:
            domain = config_path.split('/')[-1]
            print(f"\nProcessing {domain}...")
            
            # Read current config
            with sftp.open(config_path, 'r') as f:
                content = f.read().decode('utf-8')
            
            # Check if already has cookie fixes
            if 'proxy_set_header X-Forwarded-Host' in content:
                print(f"  {domain} already has cookie fixes, skipping")
                continue
            
            # Find location / block
            location_pattern = r'(location\s+/\s*\{[^}]+)'
            match = re.search(location_pattern, content, re.DOTALL)
            
            if not match:
                print(f"  WARNING: Could not find location / block in {domain}")
                continue
            
            location_block = match.group(1)
            
            # Check if it's a WebSocket service (has Upgrade header)
            is_websocket = 'Upgrade' in location_block or 'upgrade' in location_block
            
            # Build new location block with cookie fixes
            new_location = """location / {
        proxy_pass http://"""
            
            # Extract proxy_pass URL
            proxy_match = re.search(r'proxy_pass\s+http://([^;]+);', location_block)
            if proxy_match:
                proxy_url = proxy_match.group(1).strip()
                new_location += proxy_url + """;
        
        # Standard headers
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        
        # CRITICAL: Force HTTPS for cookies (even if request comes as HTTP internally)
        proxy_set_header X-Forwarded-Proto https;
        proxy_set_header X-Forwarded-Scheme https;
        proxy_set_header X-Forwarded-Host $host;
        proxy_set_header X-Forwarded-Port 443;
        
        # Cookie handling - ensure cookies work through proxy
        proxy_cookie_path / /;
        
        # Timeouts
        proxy_connect_timeout 60s;
        proxy_send_timeout 60s;
        proxy_read_timeout 60s;
        
        # Buffer settings
        proxy_buffering off;
        proxy_request_buffering off;
"""
            
            # Add WebSocket support if needed
            if is_websocket:
                new_location += """        # WebSocket support
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
"""
            
            new_location += "    }"
            
            # Replace location block
            new_content = re.sub(location_pattern, new_location, content, flags=re.DOTALL)
            
            # Write updated config
            with sftp.open(config_path, 'w') as f:
                f.write(new_content)
            
            print(f"  OK: Updated {domain}")
        
        # Test nginx config
        print("\nTesting Nginx configuration...")
        stdin, stdout, stderr = client.exec_command("nginx -t")
        exit_status = stdout.channel.recv_exit_status()
        output = stdout.read().decode('utf-8')
        error = stderr.read().decode('utf-8')
        
        if exit_status == 0:
            print("OK: Nginx configuration is valid")
            # Reload nginx
            stdin, stdout, stderr = client.exec_command("systemctl reload nginx")
            reload_status = stdout.channel.recv_exit_status()
            if reload_status == 0:
                print("OK: Nginx reloaded successfully")
            else:
                print(f"ERROR: Failed to reload nginx: {stderr.read().decode('utf-8')}")
                return 1
        else:
            print(f"ERROR: Nginx configuration error:")
            print(error)
            return 1
        
        sftp.close()
        client.close()
        return 0
        
    except Exception as e:
        print(f"Error: {e}", file=sys.stderr)
        import traceback
        traceback.print_exc()
        return 1

if __name__ == "__main__":
    if len(sys.argv) < 4:
        print("Usage: python _fix_nginx_cookies.py <host> <user> <password>", file=sys.stderr)
        sys.exit(1)
    
    exit_code = fix_nginx_config(sys.argv[1], sys.argv[2], sys.argv[3])
    sys.exit(exit_code)
