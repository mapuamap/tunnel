# Detailed diagnostic for vllm.denys.fast
. "$PSScriptRoot\config.ps1"

Write-Host "=== Detailed vllm.denys.fast diagnostic ===" -ForegroundColor Cyan

$domain = "vllm.denys.fast"

# Check DNS via public DNS
Write-Host "`n1. Checking DNS via Google DNS (8.8.8.8)..." -ForegroundColor Yellow
$dnsResult = nslookup $domain 8.8.8.8 2>&1
$dnsResult | Write-Host
if ($dnsResult -match "159.223.76.215") {
    Write-Host "   ✅ DNS resolves correctly to 159.223.76.215" -ForegroundColor Green
} else {
    Write-Host "   ❌ DNS does NOT resolve to 159.223.76.215!" -ForegroundColor Red
    Write-Host "   This is the problem - DNS record is missing or incorrect" -ForegroundColor Yellow
}

# Check nginx access logs
Write-Host "`n2. Recent nginx access logs for $domain..." -ForegroundColor Yellow
$accessLogs = python "$PSScriptRoot\_temp_ssh.py" $VPS_IP $VPS_USER $VPS_PASSWORD "grep '$domain' /var/log/nginx/access.log 2>/dev/null | tail -10 || echo 'NO_ACCESS_LOGS'"
if ($accessLogs -match "NO_ACCESS_LOGS") {
    Write-Host "   ⚠️  No access logs found (no requests received)" -ForegroundColor Yellow
} else {
    Write-Host "   Recent requests:" -ForegroundColor Gray
    $accessLogs | Write-Host
}

# Check nginx error logs in detail
Write-Host "`n3. Detailed nginx error logs..." -ForegroundColor Yellow
$errorLogs = python "$PSScriptRoot\_temp_ssh.py" $VPS_IP $VPS_USER $VPS_PASSWORD "tail -20 /var/log/nginx/error.log 2>/dev/null || echo 'NO_ERROR_LOG'"
if ($errorLogs -match "NO_ERROR_LOG") {
    Write-Host "   ✅ No error log file" -ForegroundColor Green
} else {
    Write-Host "   Recent errors:" -ForegroundColor Gray
    $errorLogs | Write-Host
}

# Check if service is listening on target
Write-Host "`n4. Checking if service is listening on 192.168.66.142:8000..." -ForegroundColor Yellow
$checkListen = python "$PSScriptRoot\_temp_ssh.py" $VPS_IP $VPS_USER $VPS_PASSWORD "ssh -o StrictHostKeyChecking=no root@192.168.66.142 'netstat -tlnp 2>/dev/null | grep :8000 || ss -tlnp 2>/dev/null | grep :8000 || echo NOT_LISTENING' 2>/dev/null || echo 'SSH_CHECK_FAILED'"
if ($checkListen -match "NOT_LISTENING") {
    Write-Host "   ❌ Service is NOT listening on port 8000!" -ForegroundColor Red
    Write-Host "   The vllm service might not be running" -ForegroundColor Yellow
} elseif ($checkListen -match "SSH_CHECK_FAILED") {
    Write-Host "   ⚠️  Could not check (SSH to 192.168.66.142 might not be configured)" -ForegroundColor Yellow
} else {
    Write-Host "   ✅ Service is listening:" -ForegroundColor Green
    $checkListen | Write-Host
}

# Test HTTP connection from VPS
Write-Host "`n5. Testing HTTP connection to target from VPS..." -ForegroundColor Yellow
$httpTest = python "$PSScriptRoot\_temp_ssh.py" $VPS_IP $VPS_USER $VPS_PASSWORD "curl -s -o /dev/null -w '%{http_code}' --connect-timeout 3 http://192.168.66.142:8000 2>/dev/null || echo 'CONNECTION_FAILED'"
if ($httpTest -match "CONNECTION_FAILED" -or $httpTest -eq "") {
    Write-Host "   ❌ HTTP connection failed!" -ForegroundColor Red
} elseif ($httpTest -match "^\d+$") {
    Write-Host "   ✅ HTTP connection successful (status: $httpTest)" -ForegroundColor Green
} else {
    Write-Host "   ⚠️  Unexpected response: $httpTest" -ForegroundColor Yellow
}

Write-Host "`n=== Diagnostic complete ===" -ForegroundColor Cyan
