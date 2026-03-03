# Remove a port forward
param(
    [Parameter(Mandatory=$true)]
    [string]$Domain
)

. "$PSScriptRoot\config.ps1"

Write-Host "Removing forward: $Domain" -ForegroundColor Cyan

$pythonScript = @"
import paramiko
import sys
import os

host = '$VPS_IP'
user = '$VPS_USER'
password = '$VPS_PASSWORD'
config_path = '$NGINX_CONFIG_DIR/$Domain'
enabled_path = '$NGINX_ENABLED_DIR/$Domain'

client = paramiko.SSHClient()
client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
client.connect(host, username=user, password=password, timeout=10)

# Remove symlink
if os.path.exists(enabled_path):
    stdin, stdout, stderr = client.exec_command(f'rm -f {enabled_path}')
    stdout.channel.recv_exit_status()

# Remove config file
sftp = client.open_sftp()
try:
    sftp.remove(config_path)
except FileNotFoundError:
    pass

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
    print('OK: Forward removed and nginx reloaded')
else:
    print(f'ERROR: Failed to reload nginx')
    sys.exit(1)
"@

$tempFile = [System.IO.Path]::GetTempFileName() + ".py"
$pythonScript | Out-File -FilePath $tempFile -Encoding UTF8

try {
    $result = python $tempFile
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "OK: Forward removed successfully" -ForegroundColor Green
    } else {
        Write-Error "Failed to remove forward: $result"
        exit 1
    }
} finally {
    Remove-Item $tempFile -ErrorAction SilentlyContinue
}
