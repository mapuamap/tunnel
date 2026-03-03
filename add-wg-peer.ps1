# Add WireGuard peer to server configuration
param(
    [Parameter(Mandatory=$true)]
    [string]$ClientPublicKey
)

. "$PSScriptRoot\config.ps1"

Write-Host "Adding WireGuard peer to server..." -ForegroundColor Cyan

$pythonScript = @"
import paramiko
import sys

host = '$VPS_IP'
user = '$VPS_USER'
password = '$VPS_PASSWORD'
client_public_key = '$ClientPublicKey'

client = paramiko.SSHClient()
client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
client.connect(host, username=user, password=password, timeout=10)

# Read current config
sftp = client.open_sftp()
with sftp.open('/etc/wireguard/wg0.conf', 'r') as f:
    config = f.read().decode('utf-8')

# Check if peer already exists
if '[Peer]' in config and client_public_key in config:
    print('WARNING: Peer with this public key already exists')
    sys.exit(1)

# Add peer configuration
peer_config = f'''

[Peer]
PublicKey = {client_public_key}
AllowedIPs = 10.0.0.2/32, 192.168.66.0/24
'''

# Remove placeholder comment if exists
config = config.replace('# Peer will be added after client setup\n# [Peer]\n# PublicKey = <CLIENT_PUBLIC_KEY>\n# AllowedIPs = 10.0.0.2/32, 192.168.66.0/24', '')

# Add peer
config += peer_config

# Write updated config
with sftp.open('/etc/wireguard/wg0.conf', 'w') as f:
    f.write(config)

# Restart WireGuard
stdin, stdout, stderr = client.exec_command('wg-quick down wg0 && wg-quick up wg0')
exit_status = stdout.channel.recv_exit_status()
error = stderr.read().decode('utf-8')

sftp.close()
client.close()

if exit_status == 0:
    print('OK: Peer added and WireGuard restarted')
else:
    print(f'ERROR: Failed to restart WireGuard: {error}')
    sys.exit(1)
"@

$tempFile = [System.IO.Path]::GetTempFileName() + ".py"
$pythonScript | Out-File -FilePath $tempFile -Encoding UTF8

try {
    $result = python $tempFile
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "OK: Peer added successfully" -ForegroundColor Green
        Write-Host "Testing connection..." -ForegroundColor Cyan
        Start-Sleep -Seconds 2
        
        $pingResult = python "$PSScriptRoot\_temp_ssh.py" $VPS_IP $VPS_USER $VPS_PASSWORD "ping -c 2 10.0.0.2"
        if ($pingResult -match "2 received") {
            Write-Host "OK: WireGuard tunnel is working!" -ForegroundColor Green
        } else {
            Write-Warning "Tunnel may not be fully established. Check LXC WireGuard status."
        }
    } else {
        Write-Error "Failed to add peer: $result"
        exit 1
    }
} finally {
    Remove-Item $tempFile -ErrorAction SilentlyContinue
}
