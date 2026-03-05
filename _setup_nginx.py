#!/usr/bin/env python3
"""Setup Nginx configurations on DO VM"""
import sys
import subprocess

try:
    import paramiko
except ImportError:
    subprocess.check_call([sys.executable, "-m", "pip", "install", "paramiko", "--quiet"])
    import paramiko

# Service configurations
SERVICES = [
    ("n8n.denys.fast", "192.168.66.150", 5678, True),  # websocket support
    ("gpu.denys.fast", "192.168.66.142", 5000, False),
    ("apigencomf.denys.fast", "192.168.66.142", 8188, False),
    ("simuqlator.denys.fast", "192.168.66.154", 80, False),
    ("tg.denys.fast", "192.168.66.154", 5000, False),
    ("llm.denys.fast", "192.168.66.154", 3000, False),
    ("interfaceblob.denys.fast", "192.168.66.154", 6001, False),
    ("comfy1.denys.fast", "192.168.66.142", 8187, False),
    ("vllm.denys.fast", "192.168.66.142", 8000, False),
    ("server1.denys.fast", "192.168.66.141", 8006, False),
    ("server1gpu.denys.fast", "192.168.66.142", 5001, False),
]

def generate_nginx_config(domain, target_ip, target_port, websocket=False):
    """Generate Nginx server block"""
    ws_config = ""
    if websocket:
        ws_config = """        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";"""
    
    return f"""server {{
    listen 80;
    server_name {domain};
    
    location / {{
        proxy_pass http://{target_ip}:{target_port};
        
        # Standard headers
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        
        # CRITICAL: Force HTTPS for cookies (even if request comes as HTTP internally)
        proxy_set_header X-Forwarded-Proto https;
        proxy_set_header X-Forwarded-Scheme https;
        proxy_set_header X-Forwarded-Host $host;
        proxy_set_header X-Forwarded-Port 443;
        
        # Cookie handling
        proxy_cookie_path / /;
        
        # Timeouts
        proxy_connect_timeout 60s;
        proxy_send_timeout 60s;
        proxy_read_timeout 60s;
        
        # Prevent stale cache after redeploy
        proxy_hide_header Cache-Control;
        add_header Cache-Control "no-cache";
        
        # Buffer settings
        proxy_buffering off;
        proxy_request_buffering off;
{ws_config}
    }}
}}
"""

def setup_nginx(host, user, password):
    """Setup all Nginx configurations"""
    client = paramiko.SSHClient()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    
    try:
        client.connect(host, username=user, password=password, timeout=10)
        sftp = client.open_sftp()
        
        # Create all config files
        for domain, target_ip, target_port, websocket in SERVICES:
            config = generate_nginx_config(domain, target_ip, target_port, websocket)
            
            # Write config file
            config_path = f"/etc/nginx/sites-available/{domain}"
            with sftp.open(config_path, 'w') as f:
                f.write(config)
            
            # Create symlink
            stdin, stdout, stderr = client.exec_command(
                f"ln -sf {config_path} /etc/nginx/sites-enabled/{domain}"
            )
            stdout.channel.recv_exit_status()
            print(f"OK: Created config for {domain}")
        
        # Test nginx config
        stdin, stdout, stderr = client.exec_command("nginx -t")
        exit_status = stdout.channel.recv_exit_status()
        output = stdout.read().decode('utf-8')
        error = stderr.read().decode('utf-8')
        
        if exit_status == 0:
            print("\nOK: Nginx configuration is valid")
            # Reload nginx
            stdin, stdout, stderr = client.exec_command("systemctl reload nginx")
            stdout.channel.recv_exit_status()
            print("OK: Nginx reloaded")
        else:
            print(f"\nERROR: Nginx configuration error:")
            print(error)
            return 1
        
        sftp.close()
        client.close()
        return 0
        
    except Exception as e:
        print(f"Error: {e}", file=sys.stderr)
        return 1

if __name__ == "__main__":
    if len(sys.argv) < 4:
        print("Usage: python _setup_nginx.py <host> <user> <password>", file=sys.stderr)
        sys.exit(1)
    
    exit_code = setup_nginx(sys.argv[1], sys.argv[2], sys.argv[3])
    sys.exit(exit_code)
