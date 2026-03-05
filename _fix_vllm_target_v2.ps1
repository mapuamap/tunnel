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

# Update config with new target using Python
Write-Host "`n2. Updating config to use $newTarget..." -ForegroundColor Yellow
$pythonScript = @"
import re

config = '''$currentConfig'''

# Replace the proxy_pass line
new_config = re.sub(
    r'proxy_pass http://192\.168\.66\.142:8000;',
    'proxy_pass http://$newTarget;',
    config
)

print(new_config)
"@

$newConfig = python "$PSScriptRoot\_temp_ssh.py" $VPS_IP $VPS_USER $VPS_PASSWORD "python3 << 'PYEOF'
$pythonScript
PYEOF
"

# Write new config using Python
Write-Host "`n3. Writing new config..." -ForegroundColor Yellow
$writeScript = @"
config = '''$newConfig'''

with open('$configPath', 'w') as f:
    f.write(config)

print('Config written successfully')
"@

python "$PSScriptRoot\_temp_ssh.py" $VPS_IP $VPS_USER $VPS_PASSWORD "python3 << 'PYEOF'
$writeScript
PYEOF
"

# Test nginx config
Write-Host "`n4. Testing nginx configuration..." -ForegroundColor Yellow
$nginxTest = python "$PSScriptRoot\_temp_ssh.py" $VPS_IP $VPS_USER $VPS_PASSWORD "nginx -t 2>&1"
if ($nginxTest -match "syntax is ok" -and $nginxTest -match "test is successful") {
    Write-Host "   ✅ Nginx config is valid" -ForegroundColor Green
    
    # Reload nginx
    Write-Host "`n5. Reloading nginx..." -ForegroundColor Yellow
    python "$PSScriptRoot\_temp_ssh.py" $VPS_IP $VPS_USER $VPS_PASSWORD "systemctl reload nginx"
    Write-Host "   ✅ Nginx reloaded" -ForegroundColor Green
    
    Write-Host "`n✅ Fix complete! vllm.denys.fast should now work." -ForegroundColor Green
} else {
    Write-Host "   ❌ Nginx config has errors!" -ForegroundColor Red
    $nginxTest | Write-Host
    Write-Host "`n⚠️  Config was updated but nginx test failed. Please check manually." -ForegroundColor Yellow
}

# Show new config
Write-Host "`n6. New config:" -ForegroundColor Yellow
$newConfigContent = python "$PSScriptRoot\_temp_ssh.py" $VPS_IP $VPS_USER $VPS_PASSWORD "cat $configPath"
$newConfigContent | Write-Host
