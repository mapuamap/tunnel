#!/usr/bin/env python3
"""Add Basic HTTP Authentication to Nginx configuration"""
import sys
import subprocess
import re

try:
    import paramiko
except ImportError:
    subprocess.check_call([sys.executable, "-m", "pip", "install", "paramiko", "--quiet"])
    import paramiko

def add_auth_to_domain(host, user, password, domain, auth_user, auth_password, auth_realm="Restricted Access"):
    """Add Basic Auth to an existing Nginx domain configuration"""
    client = paramiko.SSHClient()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    
    try:
        client.connect(host, username=user, password=password, timeout=10)
        
        config_path = f"/etc/nginx/sites-available/{domain}"
        htpasswd_path = f"/etc/nginx/.htpasswd_{domain}"
        
        # Read current config
        sftp = client.open_sftp()
        try:
            with sftp.open(config_path, 'r') as f:
                config_content = f.read().decode('utf-8')
        except FileNotFoundError:
            print(f"ERROR: Configuration file {config_path} not found", file=sys.stderr)
            return 1
        
        # Check if auth is already configured
        if 'auth_basic' in config_content:
            print(f"WARNING: Basic Auth is already configured for {domain}")
            print("Updating existing configuration...")
            # Remove existing auth directives to replace them
            config_content = re.sub(r'\s*# Basic HTTP Authentication\s*\n', '', config_content)
            config_content = re.sub(r'\s*auth_basic\s+"[^"]*";\s*\n', '', config_content)
            config_content = re.sub(r'\s*auth_basic_user_file\s+[^;]+;\s*\n', '', config_content)
        
        # Create htpasswd file
        # Use htpasswd command to create password file
        stdin, stdout, stderr = client.exec_command(
            f'which htpasswd || echo "htpasswd not found"'
        )
        htpasswd_exists = stdout.read().decode('utf-8').strip()
        
        if 'not found' in htpasswd_exists:
            # Install apache2-utils if not present
            print("Installing apache2-utils...")
            stdin, stdout, stderr = client.exec_command(
                'apt-get update && apt-get install -y apache2-utils'
            )
            stdout.channel.recv_exit_status()
        
        # Create or update htpasswd file
        # Check if file exists to determine if we should use -c (create) or not
        stdin, stdout, stderr = client.exec_command(f'test -f {htpasswd_path} && echo "EXISTS" || echo "NOT_FOUND"')
        file_exists = stdout.read().decode('utf-8').strip()
        
        # Create or update htpasswd file
        # Use -c flag only if file doesn't exist, -b for batch mode (password from command line)
        if 'EXISTS' in file_exists:
            # Update existing file (add new user or update existing)
            stdin, stdout, stderr = client.exec_command(
                f'htpasswd -b {htpasswd_path} {auth_user} {auth_password}'
            )
        else:
            # Create new file
            stdin, stdout, stderr = client.exec_command(
                f'htpasswd -cb {htpasswd_path} {auth_user} {auth_password}'
            )
        exit_status = stdout.channel.recv_exit_status()
        error = stderr.read().decode('utf-8')
        
        if exit_status != 0:
            print(f"ERROR: Failed to create htpasswd file: {error}", file=sys.stderr)
            return 1
        
        # Set proper permissions
        stdin, stdout, stderr = client.exec_command(f'chmod 644 {htpasswd_path}')
        stdin, stdout, stderr = client.exec_command(f'chown root:root {htpasswd_path}')
        
        # Modify Nginx config to add auth
        # Find location blocks and add auth_basic directives
        # Pattern: location / { ... }
        location_pattern = r'(location\s+/\s*\{[^}]*?)(proxy_pass[^;]+;)'
        
        def add_auth_to_location(match):
            location_start = match.group(1)
            proxy_pass = match.group(2)
            
            # Check if auth_basic already exists
            if 'auth_basic' in location_start:
                return match.group(0)
            
            # Add auth directives before proxy_pass
            auth_directives = f"""        # Basic HTTP Authentication
        auth_basic "{auth_realm}";
        auth_basic_user_file {htpasswd_path};
        
        {proxy_pass}"""
            
            return location_start + auth_directives
        
        # Replace in config
        new_config = re.sub(location_pattern, add_auth_to_location, config_content, flags=re.DOTALL)
        
        # If no match found, try simpler pattern - add after location {
        if new_config == config_content:
            # Try to find location / { and add auth right after opening brace
            location_simple = r'(location\s+/\s*\{)'
            def add_auth_after_location(match):
                return f'{match.group(1)}\n        # Basic HTTP Authentication\n        auth_basic "{auth_realm}";\n        auth_basic_user_file {htpasswd_path};'
            
            new_config = re.sub(location_simple, add_auth_after_location, config_content)
        
        # If still no change, add auth at the beginning of location block
        if new_config == config_content:
            # Find first location block and insert auth
            lines = config_content.split('\n')
            new_lines = []
            in_location = False
            auth_added = False
            
            for i, line in enumerate(lines):
                new_lines.append(line)
                
                # Detect location block start
                if re.match(r'\s*location\s+/\s*\{', line):
                    in_location = True
                    continue
                
                # Add auth after location { and before any proxy directives
                if in_location and not auth_added:
                    if 'proxy_' in line or line.strip() == '':
                        # Insert auth before proxy directives
                        indent = ' ' * 8  # Standard indent for location blocks
                        new_lines.insert(-1, f'{indent}# Basic HTTP Authentication')
                        new_lines.insert(-1, f'{indent}auth_basic "{auth_realm}";')
                        new_lines.insert(-1, f'{indent}auth_basic_user_file {htpasswd_path};')
                        new_lines.insert(-1, '')
                        auth_added = True
                        in_location = False
            
            new_config = '\n'.join(new_lines)
        
        # Write updated config
        with sftp.open(config_path, 'w') as f:
            f.write(new_config)
        
        sftp.close()
        
        # Test nginx config
        stdin, stdout, stderr = client.exec_command("nginx -t")
        exit_status = stdout.channel.recv_exit_status()
        error = stderr.read().decode('utf-8')
        output = stdout.read().decode('utf-8')
        
        if exit_status != 0:
            print(f"ERROR: Nginx configuration test failed:", file=sys.stderr)
            print(error, file=sys.stderr)
            # Restore original config
            with sftp.open(config_path, 'w') as f:
                f.write(config_content)
            sftp.close()
            return 1
        
        # Reload nginx
        stdin, stdout, stderr = client.exec_command("systemctl reload nginx")
        reload_status = stdout.channel.recv_exit_status()
        
        if reload_status == 0:
            print(f"OK: Basic Auth added to {domain}")
            print(f"Username: {auth_user}")
            print(f"Password file: {htpasswd_path}")
            return 0
        else:
            print(f"ERROR: Failed to reload nginx", file=sys.stderr)
            return 1
        
        client.close()
        
    except Exception as e:
        print(f"Error: {e}", file=sys.stderr)
        import traceback
        traceback.print_exc()
        return 1

if __name__ == "__main__":
    if len(sys.argv) < 7:
        print("Usage: python _add_auth.py <host> <user> <password> <domain> <auth_user> <auth_password> [realm]", file=sys.stderr)
        sys.exit(1)
    
    host = sys.argv[1]
    ssh_user = sys.argv[2]
    ssh_password = sys.argv[3]
    domain = sys.argv[4]
    auth_user = sys.argv[5]
    auth_password = sys.argv[6]
    auth_realm = sys.argv[7] if len(sys.argv) > 7 else "Restricted Access"
    
    exit_code = add_auth_to_domain(host, ssh_user, ssh_password, domain, auth_user, auth_password, auth_realm)
    sys.exit(exit_code)
