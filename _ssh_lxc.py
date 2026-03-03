#!/usr/bin/env python3
"""SSH connection to LXC using key authentication"""
import subprocess
import sys

def ssh_execute(host, user, command):
    """Execute command via SSH using key authentication"""
    ssh_cmd = [
        "ssh",
        "-o", "StrictHostKeyChecking=no",
        "-o", "ConnectTimeout=10",
        "-o", "BatchMode=yes",
        f"{user}@{host}",
        command
    ]
    
    try:
        result = subprocess.run(
            ssh_cmd,
            capture_output=True,
            text=True,
            timeout=30
        )
        
        if result.stdout:
            print(result.stdout, end='')
        if result.stderr:
            print(result.stderr, end='', file=sys.stderr)
        
        return result.returncode
    except subprocess.TimeoutExpired:
        print("SSH connection timeout", file=sys.stderr)
        return 1
    except Exception as e:
        print(f"SSH connection failed: {e}", file=sys.stderr)
        return 1

if __name__ == "__main__":
    if len(sys.argv) < 3:
        print("Usage: python _ssh_lxc.py <host> <user> <command>", file=sys.stderr)
        sys.exit(1)
    
    host = sys.argv[1]
    user = sys.argv[2]
    command = " ".join(sys.argv[3:])
    
    exit_code = ssh_execute(host, user, command)
    sys.exit(exit_code)
