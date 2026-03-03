#!/usr/bin/env python3
"""Temporary SSH connection script for automation"""
import sys
import subprocess

try:
    import paramiko
except ImportError:
    print("Installing paramiko...", file=sys.stderr)
    subprocess.check_call([sys.executable, "-m", "pip", "install", "paramiko", "--quiet"])
    import paramiko

def ssh_execute(host, user, password, command):
    """Execute command via SSH"""
    client = paramiko.SSHClient()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    
    try:
        # Try with key first if no password provided
        if not password or password == "":
            # Use default SSH key
            import os
            key_path = os.path.expanduser("~/.ssh/id_rsa")
            if os.path.exists(key_path):
                private_key = paramiko.RSAKey.from_private_key_file(key_path)
                client.connect(host, username=user, pkey=private_key, timeout=10)
            else:
                # Fallback to password auth
                client.connect(host, username=user, password="", timeout=10, allow_agent=True)
        else:
            client.connect(host, username=user, password=password, timeout=10)
        
        stdin, stdout, stderr = client.exec_command(command)
        exit_status = stdout.channel.recv_exit_status()
        output = stdout.read().decode('utf-8')
        error = stderr.read().decode('utf-8')
        
        if output:
            print(output, end='')
        if error:
            print(error, end='', file=sys.stderr)
        
        client.close()
        return exit_status
    except Exception as e:
        print(f"SSH connection failed: {e}", file=sys.stderr)
        return 1

if __name__ == "__main__":
    if len(sys.argv) < 4:
        print("Usage: python _temp_ssh.py <host> <user> [password] <command>", file=sys.stderr)
        sys.exit(1)
    
    host = sys.argv[1]
    user = sys.argv[2]
    # Password is optional - if not provided, use SSH key
    if len(sys.argv) >= 5:
        password = sys.argv[3]
        command = " ".join(sys.argv[4:])
    else:
        password = ""
        command = " ".join(sys.argv[3:])
    
    exit_code = ssh_execute(host, user, password, command)
    sys.exit(exit_code)
