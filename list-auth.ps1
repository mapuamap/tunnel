# List domains with Basic HTTP Authentication
. "$PSScriptRoot\config.ps1"

Write-Host "Checking domains with Basic Auth..." -ForegroundColor Cyan

$pythonScript = @"
import paramiko
import os
import sys

host = '$VPS_IP'
user = '$VPS_USER'
password = '$VPS_PASSWORD'

client = paramiko.SSHClient()
client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
client.connect(host, username=user, password=password, timeout=10)

# List all nginx configs
stdin, stdout, stderr = client.exec_command('ls /etc/nginx/sites-available/')
configs = stdout.read().decode('utf-8').strip().split('\n')

domains_with_auth = []

for config_file in configs:
    if not config_file.strip():
        continue
    
    domain = config_file.strip()
    config_path = f'/etc/nginx/sites-available/{domain}'
    
    # Read config
    stdin, stdout, stderr = client.exec_command(f'cat {config_path}')
    config_content = stdout.read().decode('utf-8')
    
    if 'auth_basic' in config_content:
        # Extract realm if available
        import re
        realm_match = re.search(r'auth_basic\s+"([^"]+)"', config_content)
        realm = realm_match.group(1) if realm_match else 'Not specified'
        
        # Check htpasswd file
        htpasswd_path = f'/etc/nginx/.htpasswd_{domain}'
        stdin, stdout, stderr = client.exec_command(f'test -f {htpasswd_path} && echo "EXISTS" || echo "NOT_FOUND"')
        htpasswd_exists = stdout.read().decode('utf-8').strip()
        
        domains_with_auth.append({
            'domain': domain,
            'realm': realm,
            'htpasswd': 'EXISTS' if 'EXISTS' in htpasswd_exists else 'NOT_FOUND'
        })

client.close()

if domains_with_auth:
    print('Domains with Basic Auth:')
    print('=' * 60)
    for item in domains_with_auth:
        print(f"Domain: {item['domain']}")
        print(f"  Realm: {item['realm']}")
        print(f"  Password file: {item['htpasswd']}")
        print()
else:
    print('No domains with Basic Auth configured')
"@

$tempFile = [System.IO.Path]::GetTempFileName() + ".py"
$pythonScript | Out-File -FilePath $tempFile -Encoding UTF8

try {
    python $tempFile
} finally {
    Remove-Item $tempFile -ErrorAction SilentlyContinue
}
