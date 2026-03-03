#!/usr/bin/env python3
"""Setup WireGuard client on LXC"""
import sys
import subprocess

try:
    import paramiko
except ImportError:
    subprocess.check_call([sys.executable, "-m", "pip", "install", "paramiko", "--quiet"])
    import paramiko

def setup_wg_client(host, user):
    """Setup WireGuard client configuration"""
    client = paramiko.SSHClient()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    
    try:
        # Use SSH key authentication
        import os
        key_path = os.path.expanduser("~/.ssh/id_rsa")
        if os.path.exists(key_path):
            private_key = paramiko.RSAKey.from_private_key_file(key_path)
            client.connect(host, username=user, pkey=private_key, timeout=10)
        else:
            print("SSH key not found, trying password auth...", file=sys.stderr)
            return 1
        
        # Read private key
        stdin, stdout, stderr = client.exec_command("cat /etc/wireguard/client_private.key")
        private_key_content = stdout.read().decode('utf-8').strip()
        
        if not private_key_content:
            print("ERROR: Could not read private key", file=sys.stderr)
            return 1
        
        # Create configuration
        config = f"""[Interface]
Address = 10.0.0.2/24
PrivateKey = {private_key_content}

[Peer]
PublicKey = I3fb8ObvsS+yDUZ1URkpZPgApnOuY/hfxowimbPa0nM=
Endpoint = 159.223.76.215:51820
AllowedIPs = 10.0.0.0/24
PersistentKeepalive = 25
"""
        
        # Write configuration
        sftp = client.open_sftp()
        with sftp.open('/etc/wireguard/wg0.conf', 'w') as f:
            f.write(config)
        sftp.close()
        
        # Start WireGuard
        stdin, stdout, stderr = client.exec_command("systemctl start wg-quick@wg0")
        exit_status = stdout.channel.recv_exit_status()
        
        if exit_status != 0:
            error = stderr.read().decode('utf-8')
            print(f"ERROR starting WireGuard: {error}", file=sys.stderr)
            # Check logs
            stdin, stdout, stderr = client.exec_command("journalctl -xeu wg-quick@wg0.service --no-pager -n 10")
            print(stdout.read().decode('utf-8'), file=sys.stderr)
            return 1
        
        # Show status
        stdin, stdout, stderr = client.exec_command("wg show")
        output = stdout.read().decode('utf-8')
        print(output)
        
        client.close()
        return 0
        
    except Exception as e:
        print(f"Error: {e}", file=sys.stderr)
        return 1

if __name__ == "__main__":
    if len(sys.argv) < 3:
        print("Usage: python _setup_wg_client.py <host> <user>", file=sys.stderr)
        sys.exit(1)
    
    exit_code = setup_wg_client(sys.argv[1], sys.argv[2])
    sys.exit(exit_code)
