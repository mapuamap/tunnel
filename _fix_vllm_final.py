#!/usr/bin/env python3
import paramiko
import sys

host = "159.223.76.215"
user = "root"
password = "_X6NE_Eqz?hkGn"
config_path = "/etc/nginx/sites-available/vllm.denys.fast"

config_content = """server {
    listen 80;
    server_name vllm.denys.fast;
    
    location / {
        proxy_pass http://192.168.66.142:8000;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;

        # Prevent stale cache after redeploy
        proxy_hide_header Cache-Control;
        add_header Cache-Control "no-cache";
    }
}
"""

try:
    ssh = paramiko.SSHClient()
    ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    ssh.connect(host, username=user, password=password, timeout=10)
    
    # Write config file
    sftp = ssh.open_sftp()
    with sftp.file(config_path, 'w') as f:
        f.write(config_content)
    sftp.close()
    
    print("[OK] Config file written successfully")
    
    # Test nginx config
    stdin, stdout, stderr = ssh.exec_command("nginx -t")
    exit_status = stdout.channel.recv_exit_status()
    output = stdout.read().decode() + stderr.read().decode()
    
    if exit_status == 0:
        print("[OK] Nginx config test passed")
        
        # Reload nginx
        stdin, stdout, stderr = ssh.exec_command("systemctl reload nginx")
        exit_status = stdout.channel.recv_exit_status()
        if exit_status == 0:
            print("[OK] Nginx reloaded successfully")
            print("\n[OK] Fix complete! vllm.denys.fast should now work.")
        else:
            print("[ERROR] Failed to reload nginx")
            print(stderr.read().decode())
    else:
        print("[ERROR] Nginx config test failed:")
        print(output)
    
    ssh.close()
    
except Exception as e:
    print(f"[ERROR] Error: {e}")
    sys.exit(1)
