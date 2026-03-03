# Show tunnel status
param()

. "$PSScriptRoot\config.ps1"

Write-Host "Tunnel Status" -ForegroundColor Cyan
Write-Host ("=" * 80)

# Check WireGuard status
Write-Host "`nWireGuard Status:" -ForegroundColor Yellow
$wgStatus = python "$PSScriptRoot\_temp_ssh.py" $VPS_IP $VPS_USER $VPS_PASSWORD "wg show 2>&1"
if ($wgStatus -match "interface: wg0") {
    Write-Host "OK: WireGuard is running" -ForegroundColor Green
    $wgStatus | Select-String -Pattern "interface:|peer:|endpoint:|allowed ips:" | ForEach-Object {
        Write-Host "  $_"
    }
} else {
    Write-Host "WARNING: WireGuard is not running" -ForegroundColor Yellow
    Write-Host $wgStatus
}

# Check Nginx status
Write-Host "`nNginx Status:" -ForegroundColor Yellow
$nginxStatus = python "$PSScriptRoot\_temp_ssh.py" $VPS_IP $VPS_USER $VPS_PASSWORD "systemctl is-active nginx"
if ($nginxStatus.Trim() -eq "active") {
    Write-Host "OK: Nginx is running" -ForegroundColor Green
} else {
    Write-Host "ERROR: Nginx is not running" -ForegroundColor Red
}

# Check SSL certificates
Write-Host "`nSSL Certificates:" -ForegroundColor Yellow
$certInfo = python "$PSScriptRoot\_temp_ssh.py" $VPS_IP $VPS_USER $VPS_PASSWORD @"
import os
import subprocess
import json

certs = []
cert_dir = '/etc/letsencrypt/live'

if os.path.exists(cert_dir):
    for domain_dir in os.listdir(cert_dir):
        domain_path = os.path.join(cert_dir, domain_dir)
        if os.path.isdir(domain_path):
            cert_file = os.path.join(domain_path, 'cert.pem')
            if os.path.exists(cert_file):
                result = subprocess.run(
                    ['openssl', 'x509', '-noout', '-subject', '-dates', '-in', cert_file],
                    capture_output=True,
                    text=True
                )
                if result.returncode == 0:
                    output = result.stdout
                    subject = [l for l in output.split('\n') if l.startswith('subject=')][0]
                    not_after = [l for l in output.split('\n') if l.startswith('notAfter=')][0]
                    certs.append({
                        'domain': domain_dir,
                        'expires': not_after.replace('notAfter=', '')
                    })

print(json.dumps(certs, indent=2))
"@

try {
    $certs = $certInfo | ConvertFrom-Json
    if ($certs.Count -gt 0) {
        foreach ($cert in $certs) {
            Write-Host "  $($cert.domain): expires $($cert.expires)" -ForegroundColor Green
        }
    } else {
        Write-Host "  No SSL certificates found" -ForegroundColor Yellow
    }
} catch {
    Write-Host "  Could not retrieve certificate info" -ForegroundColor Yellow
}

# List active forwards
Write-Host "`nActive Forwards:" -ForegroundColor Yellow
& "$PSScriptRoot\list-forwards.ps1"
