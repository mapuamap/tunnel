# Diagnostic script for vllm.denys.fast
. "$PSScriptRoot\config.ps1"

Write-Host "=== Checking vllm.denys.fast configuration ===" -ForegroundColor Cyan

$domain = "vllm.denys.fast"
$expectedTarget = "192.168.66.142:8000"

# Check if nginx config exists
Write-Host "`n1. Checking nginx config file..." -ForegroundColor Yellow
$checkConfig = python "$PSScriptRoot\_temp_ssh.py" $VPS_IP $VPS_USER $VPS_PASSWORD "cat /etc/nginx/sites-available/$domain 2>/dev/null || echo 'CONFIG_NOT_FOUND'"
if ($checkConfig -match "CONFIG_NOT_FOUND") {
    Write-Host "   ❌ Config file NOT FOUND!" -ForegroundColor Red
} else {
    Write-Host "   ✅ Config file exists" -ForegroundColor Green
    Write-Host "   Content:" -ForegroundColor Gray
    $checkConfig | Write-Host
}

# Check if config is enabled
Write-Host "`n2. Checking if config is enabled..." -ForegroundColor Yellow
$checkEnabled = python "$PSScriptRoot\_temp_ssh.py" $VPS_IP $VPS_USER $VPS_PASSWORD "ls -la /etc/nginx/sites-enabled/$domain 2>/dev/null || echo 'NOT_ENABLED'"
if ($checkEnabled -match "NOT_ENABLED") {
    Write-Host "   ❌ Config is NOT ENABLED (no symlink in sites-enabled)!" -ForegroundColor Red
} else {
    Write-Host "   ✅ Config is enabled" -ForegroundColor Green
}

# Check nginx test
Write-Host "`n3. Testing nginx configuration..." -ForegroundColor Yellow
$nginxTest = python "$PSScriptRoot\_temp_ssh.py" $VPS_IP $VPS_USER $VPS_PASSWORD "nginx -t 2>&1"
if ($nginxTest -match "syntax is ok" -and $nginxTest -match "test is successful") {
    Write-Host "   ✅ Nginx config is valid" -ForegroundColor Green
} else {
    Write-Host "   ❌ Nginx config has errors!" -ForegroundColor Red
    $nginxTest | Write-Host
}

# Check if target is reachable from VPS
Write-Host "`n4. Checking if target $expectedTarget is reachable from VPS..." -ForegroundColor Yellow
$checkTarget = python "$PSScriptRoot\_temp_ssh.py" $VPS_IP $VPS_USER $VPS_PASSWORD "timeout 2 bash -c 'echo > /dev/tcp/192.168.66.142/8000' 2>/dev/null && echo 'REACHABLE' || echo 'NOT_REACHABLE'"
if ($checkTarget -match "REACHABLE") {
    Write-Host "   ✅ Target is reachable" -ForegroundColor Green
} else {
    Write-Host "   ❌ Target is NOT reachable from VPS!" -ForegroundColor Red
    Write-Host "   This means the service on 192.168.66.142:8000 is not running or not accessible" -ForegroundColor Yellow
}

# Check DNS resolution
Write-Host "`n5. Checking DNS resolution..." -ForegroundColor Yellow
$dnsCheck = nslookup $domain 2>&1
if ($dnsCheck -match "159.223.76.215") {
    Write-Host "   ✅ DNS resolves to 159.223.76.215" -ForegroundColor Green
} else {
    Write-Host "   ⚠️  DNS might not be configured correctly" -ForegroundColor Yellow
    $dnsCheck | Write-Host
}

# Check nginx status
Write-Host "`n6. Checking nginx service status..." -ForegroundColor Yellow
$nginxStatus = python "$PSScriptRoot\_temp_ssh.py" $VPS_IP $VPS_USER $VPS_PASSWORD "systemctl is-active nginx && echo 'ACTIVE' || echo 'INACTIVE'"
if ($nginxStatus -match "ACTIVE") {
    Write-Host "   ✅ Nginx is running" -ForegroundColor Green
} else {
    Write-Host "   ❌ Nginx is NOT running!" -ForegroundColor Red
}

# Check recent nginx error logs
Write-Host "`n7. Recent nginx error logs for $domain..." -ForegroundColor Yellow
$errorLogs = python "$PSScriptRoot\_temp_ssh.py" $VPS_IP $VPS_USER $VPS_PASSWORD "grep '$domain' /var/log/nginx/error.log 2>/dev/null | tail -5 || echo 'NO_ERRORS'"
if ($errorLogs -match "NO_ERRORS") {
    Write-Host "   ✅ No recent errors" -ForegroundColor Green
} else {
    Write-Host "   ⚠️  Recent errors found:" -ForegroundColor Yellow
    $errorLogs | Write-Host
}

Write-Host "`n=== Diagnostic complete ===" -ForegroundColor Cyan
