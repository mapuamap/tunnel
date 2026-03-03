# Update password for existing Basic Auth user or add new user
param(
    [Parameter(Mandatory=$true)]
    [string]$Domain,
    
    [Parameter(Mandatory=$true)]
    [string]$Username,
    
    [Parameter(Mandatory=$true)]
    [string]$Password
)

. "$PSScriptRoot\config.ps1"

Write-Host "Updating Basic Auth for: $Domain" -ForegroundColor Cyan
Write-Host "Username: $Username" -ForegroundColor Cyan

# Validate domain format
if ($Domain -notmatch '^[a-zA-Z0-9][a-zA-Z0-9\.-]+[a-zA-Z0-9]$') {
    Write-Error "Invalid domain format: $Domain"
    exit 1
}

# Update password using htpasswd
$pythonScript = @"
import paramiko
import sys

host = '$VPS_IP'
user = '$VPS_USER'
password = '$VPS_PASSWORD'
domain = '$Domain'
auth_user = '$Username'
auth_password = '$Password'
htpasswd_path = f'/etc/nginx/.htpasswd_{domain}'

client = paramiko.SSHClient()
client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
client.connect(host, username=user, password=password, timeout=10)

# Check if htpasswd file exists
stdin, stdout, stderr = client.exec_command(f'test -f {htpasswd_path} && echo "EXISTS" || echo "NOT_FOUND"')
file_exists = stdout.read().decode('utf-8').strip()

if 'NOT_FOUND' in file_exists:
    print(f'ERROR: Basic Auth not configured for {domain}. Use add-auth.ps1 first.', file=sys.stderr)
    sys.exit(1)

# Update password (htpasswd -b updates existing user or adds new one)
stdin, stdout, stderr = client.exec_command(
    f'htpasswd -b {htpasswd_path} {auth_user} {auth_password}'
)
exit_status = stdout.channel.recv_exit_status()
error = stderr.read().decode('utf-8')

client.close()

if exit_status == 0:
    print(f'OK: Password updated for user {auth_user} on {domain}')
    sys.exit(0)
else:
    print(f'ERROR: Failed to update password: {error}', file=sys.stderr)
    sys.exit(1)
"@

$tempFile = [System.IO.Path]::GetTempFileName() + ".py"
$pythonScript | Out-File -FilePath $tempFile -Encoding UTF8

try {
    $result = python $tempFile
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "OK: Password updated successfully" -ForegroundColor Green
    } else {
        Write-Error "Failed to update password: $result"
        exit 1
    }
} finally {
    Remove-Item $tempFile -ErrorAction SilentlyContinue
}
