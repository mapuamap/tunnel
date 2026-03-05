# Test vllm.denys.fast directly
. "$PSScriptRoot\config.ps1"

Write-Host "=== Testing vllm.denys.fast ===" -ForegroundColor Cyan

# Test HTTP connection to the domain
Write-Host "`n1. Testing HTTP connection to vllm.denys.fast..." -ForegroundColor Yellow
try {
    $response = Invoke-WebRequest -Uri "http://vllm.denys.fast" -TimeoutSec 5 -UseBasicParsing -ErrorAction Stop
    Write-Host "   ✅ Connection successful!" -ForegroundColor Green
    Write-Host "   Status: $($response.StatusCode)" -ForegroundColor Gray
    Write-Host "   Content length: $($response.Content.Length)" -ForegroundColor Gray
} catch {
    Write-Host "   ❌ Connection failed!" -ForegroundColor Red
    Write-Host "   Error: $($_.Exception.Message)" -ForegroundColor Yellow
    
    # Try with IP directly
    Write-Host "`n2. Testing direct IP connection (159.223.76.215)..." -ForegroundColor Yellow
    try {
        $response = Invoke-WebRequest -Uri "http://159.223.76.215" -Headers @{"Host"="vllm.denys.fast"} -TimeoutSec 5 -UseBasicParsing -ErrorAction Stop
        Write-Host "   ✅ Direct IP connection successful!" -ForegroundColor Green
        Write-Host "   Status: $($response.StatusCode)" -ForegroundColor Gray
        Write-Host "   This means nginx works, but DNS might not be propagating" -ForegroundColor Yellow
    } catch {
        Write-Host "   ❌ Direct IP connection also failed!" -ForegroundColor Red
        Write-Host "   Error: $($_.Exception.Message)" -ForegroundColor Yellow
        Write-Host "   This suggests nginx is not responding or not configured correctly" -ForegroundColor Yellow
    }
}

# Check if vllm should be on localhost instead
Write-Host "`n3. Checking if vllm runs on VPS localhost:8000..." -ForegroundColor Yellow
$checkLocalhost = python "$PSScriptRoot\_temp_ssh.py" $VPS_IP $VPS_USER $VPS_PASSWORD "timeout 2 bash -c 'echo > /dev/tcp/127.0.0.1/8000' 2>/dev/null && echo 'LISTENING' || echo 'NOT_LISTENING'"
if ($checkLocalhost -match "LISTENING") {
    Write-Host "   ✅ Service IS listening on localhost:8000!" -ForegroundColor Green
    Write-Host "   ⚠️  Current config points to 192.168.66.142:8000" -ForegroundColor Yellow
    Write-Host "   💡 You might need to change target to 127.0.0.1:8000 or localhost:8000" -ForegroundColor Cyan
} else {
    Write-Host "   ❌ Service is NOT listening on localhost:8000" -ForegroundColor Red
}

Write-Host "`n=== Test complete ===" -ForegroundColor Cyan
