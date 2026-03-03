# Edit an existing port forward
param(
    [Parameter(Mandatory=$true)]
    [string]$Domain,
    
    [Parameter(Mandatory=$true)]
    [string]$Target  # Format: IP:PORT
)

. "$PSScriptRoot\config.ps1"

# Validate target format
if ($Target -notmatch '^(\d+\.\d+\.\d+\.\d+):(\d+)$') {
    Write-Error "Target must be in format IP:PORT (e.g., 192.168.66.142:5000)"
    exit 1
}

Write-Host "Editing forward: $Domain -> $Target" -ForegroundColor Cyan

# Read existing config to preserve WebSocket settings
$pythonScript = @"
import paramiko
import re
import sys

host = '$VPS_IP'
user = '$VPS_USER'
password = '$VPS_PASSWORD'
config_path = '$NGINX_CONFIG_DIR/$Domain'
new_target = '$Target'

client = paramiko.SSHClient()
client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
client.connect(host, username=user, password=password, timeout=10)

# Read existing config
sftp = client.open_sftp()
try:
    with sftp.open(config_path, 'r') as f:
        content = f.read().decode('utf-8')
except FileNotFoundError:
    print(f'ERROR: Config file not found: {config_path}')
    sys.exit(1)

# Check if WebSocket is enabled
has_websocket = 'proxy_http_version 1.1' in content

# Update proxy_pass
new_target_ip, new_target_port = new_target.split(':')
content = re.sub(
    r'proxy_pass\s+http://\d+\.\d+\.\d+\.\d+:\d+;',
    f'proxy_pass http://{new_target};',
    content
)

# Write updated config
with sftp.open(config_path, 'w') as f:
    f.write(content)

# Test nginx
stdin, stdout, stderr = client.exec_command('nginx -t')
test_status = stdout.channel.recv_exit_status()
test_error = stderr.read().decode('utf-8')

if test_status != 0:
    print(f'ERROR: Nginx test failed: {test_error}')
    sys.exit(1)

# Reload nginx
stdin, stdout, stderr = client.exec_command('systemctl reload nginx')
reload_status = stdout.channel.recv_exit_status()

sftp.close()
client.close()

if reload_status == 0:
    print('OK: Forward updated and nginx reloaded')
else:
    print(f'ERROR: Failed to reload nginx')
    sys.exit(1)
"@

$tempFile = [System.IO.Path]::GetTempFileName() + ".py"
$pythonScript | Out-File -FilePath $tempFile -Encoding UTF8

try {
    $result = python $tempFile
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "OK: Forward updated successfully" -ForegroundColor Green
    } else {
        Write-Error "Failed to update forward: $result"
        exit 1
    }
} finally {
    Remove-Item $tempFile -ErrorAction SilentlyContinue
}
