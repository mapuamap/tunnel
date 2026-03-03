# Remove Basic HTTP Authentication from a domain
param(
    [Parameter(Mandatory=$true)]
    [string]$Domain
)

. "$PSScriptRoot\config.ps1"

Write-Host "Removing Basic Auth from: $Domain" -ForegroundColor Cyan

# Validate domain format
if ($Domain -notmatch '^[a-zA-Z0-9][a-zA-Z0-9\.-]+[a-zA-Z0-9]$') {
    Write-Error "Invalid domain format: $Domain"
    exit 1
}

# Check if domain configuration exists
$checkResult = python "$PSScriptRoot\_temp_ssh.py" $VPS_IP $VPS_USER $VPS_PASSWORD "test -f /etc/nginx/sites-available/$Domain && echo 'EXISTS' || echo 'NOT_FOUND'"

if ($checkResult -notmatch 'EXISTS') {
    Write-Error "Domain configuration not found: $Domain"
    exit 1
}

# Remove auth from config
$pythonScript = @"
import paramiko
import re
import sys

host = '$VPS_IP'
user = '$VPS_USER'
password = '$VPS_PASSWORD'
domain = '$Domain'
config_path = f'/etc/nginx/sites-available/{domain}'
htpasswd_path = f'/etc/nginx/.htpasswd_{domain}'

client = paramiko.SSHClient()
client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
client.connect(host, username=user, password=password, timeout=10)

# Read current config
sftp = client.open_sftp()
try:
    with sftp.open(config_path, 'r') as f:
        config_content = f.read().decode('utf-8')
except FileNotFoundError:
    print(f'ERROR: Configuration file not found', file=sys.stderr)
    sys.exit(1)

# Check if auth is configured
if 'auth_basic' not in config_content:
    print(f'WARNING: Basic Auth is not configured for {domain}')
    sftp.close()
    client.close()
    sys.exit(0)

# Remove auth_basic directives
# Remove lines containing auth_basic
lines = config_content.split('\n')
new_lines = []
skip_next = False

for line in lines:
    # Skip auth_basic and auth_basic_user_file lines
    if 'auth_basic' in line:
        continue
    # Skip comment lines about Basic Auth
    if 'Basic HTTP Authentication' in line:
        continue
    # Skip empty lines after auth directives (but keep other empty lines)
    if skip_next and line.strip() == '':
        skip_next = False
        continue
    
    new_lines.append(line)
    skip_next = False

new_config = '\n'.join(new_lines)

# Write updated config
with sftp.open(config_path, 'w') as f:
    f.write(new_config)

sftp.close()

# Test nginx config
stdin, stdout, stderr = client.exec_command('nginx -t')
exit_status = stdout.channel.recv_exit_status()
error = stderr.read().decode('utf-8')

if exit_status != 0:
    print(f'ERROR: Nginx configuration test failed: {error}', file=sys.stderr)
    sys.exit(1)

# Reload nginx
stdin, stdout, stderr = client.exec_command('systemctl reload nginx')
reload_status = stdout.channel.recv_exit_status()

# Remove htpasswd file (optional)
stdin, stdout, stderr = client.exec_command(f'rm -f {htpasswd_path}')

client.close()

if reload_status == 0:
    print(f'OK: Basic Auth removed from {domain}')
    sys.exit(0)
else:
    print(f'ERROR: Failed to reload nginx', file=sys.stderr)
    sys.exit(1)
"@

$tempFile = [System.IO.Path]::GetTempFileName() + ".py"
$pythonScript | Out-File -FilePath $tempFile -Encoding UTF8

try {
    $result = python $tempFile
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "OK: Basic Auth removed from $Domain" -ForegroundColor Green
    } else {
        Write-Error "Failed to remove Basic Auth: $result"
        exit 1
    }
} finally {
    Remove-Item $tempFile -ErrorAction SilentlyContinue
}
