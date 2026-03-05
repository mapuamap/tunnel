# Fix vllm.denys.fast target to localhost:8000
. "$PSScriptRoot\config.ps1"

Write-Host "=== Fixing vllm.denys.fast target ===" -ForegroundColor Cyan

$domain = "vllm.denys.fast"
$newTarget = "127.0.0.1:8000"
$configPath = "/etc/nginx/sites-available/$domain"

# Read current config
Write-Host "`n1. Reading current config..." -ForegroundColor Yellow
$currentConfig = python "$PSScriptRoot\_temp_ssh.py" $VPS_IP $VPS_USER $VPS_PASSWORD "cat $configPath"
Write-Host "Current config:" -ForegroundColor Gray
$currentConfig | Write-Host

# Update config with new target
Write-Host "`n2. Updating config to use $newTarget..." -ForegroundColor Yellow
$newConfig = $currentConfig -replace "proxy_pass http://192\.168\.66\.142:8000", "proxy_pass http://$newTarget"

# Write new config
python "$PSScriptRoot\_temp_ssh.py" $VPS_IP $VPS_USER $VPS_PASSWORD @"
cat > $configPath << 'EOF'
$newConfig
EOF
"@

# Test nginx config
Write-Host "`n3. Testing nginx configuration..." -ForegroundColor Yellow
$nginxTest = python "$PSScriptRoot\_temp_ssh.py" $VPS_IP $VPS_USER $VPS_PASSWORD "nginx -t 2>&1"
if ($nginxTest -match "syntax is ok" -and $nginxTest -match "test is successful") {
    Write-Host "   ✅ Nginx config is valid" -ForegroundColor Green
    
    # Reload nginx
    Write-Host "`n4. Reloading nginx..." -ForegroundColor Yellow
    python "$PSScriptRoot\_temp_ssh.py" $VPS_IP $VPS_USER $VPS_PASSWORD "systemctl reload nginx"
    Write-Host "   ✅ Nginx reloaded" -ForegroundColor Green
    
    Write-Host "`n✅ Fix complete! vllm.denys.fast should now work." -ForegroundColor Green
} else {
    Write-Host "   ❌ Nginx config has errors!" -ForegroundColor Red
    $nginxTest | Write-Host
    Write-Host "`n⚠️  Config was updated but nginx test failed. Please check manually." -ForegroundColor Yellow
}

# Show new config
Write-Host "`n5. New config:" -ForegroundColor Yellow
$newConfigContent = python "$PSScriptRoot\_temp_ssh.py" $VPS_IP $VPS_USER $VPS_PASSWORD "cat $configPath"
$newConfigContent | Write-Host
