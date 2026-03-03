# Add a new port forward
param(
    [Parameter(Mandatory=$true)]
    [string]$Domain,
    
    [Parameter(Mandatory=$true)]
    [string]$Target,  # Format: IP:PORT
    
    [switch]$WebSocket,
    
    [switch]$GetSSL
)

. "$PSScriptRoot\config.ps1"

# Validate target format
if ($Target -notmatch '^(\d+\.\d+\.\d+\.\d+):(\d+)$') {
    Write-Error "Target must be in format IP:PORT (e.g., 192.168.66.142:5000)"
    exit 1
}

$targetIp = $matches[1]
$targetPort = $matches[2]

Write-Host "Adding forward: $Domain -> $Target" -ForegroundColor Cyan

# Generate Nginx config
$wsConfig = ""
if ($WebSocket) {
    $wsConfig = @"
        proxy_http_version 1.1;
        proxy_set_header Upgrade `$http_upgrade;
        proxy_set_header Connection "upgrade";
"@
}

$nginxConfig = @"
server {
    listen 80;
    server_name $Domain;
    
    location / {
        proxy_pass http://$Target;
        proxy_set_header Host `$host;
        proxy_set_header X-Real-IP `$remote_addr;
        proxy_set_header X-Forwarded-For `$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto `$scheme;
$wsConfig
    }
}
"@

# Upload config to server
$tempFile = [System.IO.Path]::GetTempFileName()
$nginxConfig | Out-File -FilePath $tempFile -Encoding UTF8

try {
    # Create config file on server
    $configContent = Get-Content $tempFile -Raw
    $configContent = $configContent -replace "`r`n", "`n"  # Normalize line endings
    
    $pythonScript = @"
import paramiko
import sys

host = '$VPS_IP'
user = '$VPS_USER'
password = '$VPS_PASSWORD'
config_path = '$NGINX_CONFIG_DIR/$Domain'
config_content = '''$nginxConfig'''

client = paramiko.SSHClient()
client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
client.connect(host, username=user, password=password, timeout=10)

# Write config file
sftp = client.open_sftp()
with sftp.open(config_path, 'w') as f:
    f.write(config_content)

# Create symlink
stdin, stdout, stderr = client.exec_command(f'ln -sf {config_path} $NGINX_ENABLED_DIR/$Domain')
exit_status = stdout.channel.recv_exit_status()

# Test nginx
stdin, stdout, stderr = client.exec_command('nginx -t')
test_status = stdout.channel.recv_exit_status()
test_output = stdout.read().decode('utf-8')
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
    print('OK: Config created and nginx reloaded')
else:
    print(f'ERROR: Failed to reload nginx')
    sys.exit(1)
"@
    
    $pythonScript | Out-File -FilePath "$tempFile.py" -Encoding UTF8
    
    $result = python "$tempFile.py"
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "OK: Forward added successfully" -ForegroundColor Green
        
        if ($GetSSL) {
            Write-Host "Requesting SSL certificate..." -ForegroundColor Cyan
            python "$PSScriptRoot\_temp_ssh.py" $VPS_IP $VPS_USER $VPS_PASSWORD "certbot --nginx -d $Domain --non-interactive --agree-tos --email admin@denys.fast"
            
            if ($LASTEXITCODE -eq 0) {
                Write-Host "OK: SSL certificate obtained" -ForegroundColor Green
            } else {
                Write-Warning "SSL certificate request failed. You may need to update DNS first."
            }
        }
    } else {
        Write-Error "Failed to add forward: $result"
        exit 1
    }
} finally {
    Remove-Item $tempFile -ErrorAction SilentlyContinue
    Remove-Item "$tempFile.py" -ErrorAction SilentlyContinue
}
