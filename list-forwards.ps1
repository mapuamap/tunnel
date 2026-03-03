# List all configured port forwards
param()

. "$PSScriptRoot\config.ps1"

Write-Host "Fetching Nginx configurations from $VPS_IP..." -ForegroundColor Cyan

$command = @"
import json
import os
import re

configs = []
config_dir = '$NGINX_CONFIG_DIR'
enabled_dir = '$NGINX_ENABLED_DIR'

for filename in os.listdir(config_dir):
    if filename.endswith('.denys.fast'):
        config_path = os.path.join(config_dir, filename)
        enabled_path = os.path.join(enabled_dir, filename)
        is_enabled = os.path.exists(enabled_path)
        
        with open(config_path, 'r') as f:
            content = f.read()
            
        # Extract server_name
        server_name_match = re.search(r'server_name\s+(\S+);', content)
        server_name = server_name_match.group(1) if server_name_match else 'N/A'
        
        # Extract proxy_pass
        proxy_pass_match = re.search(r'proxy_pass\s+http://(\d+\.\d+\.\d+\.\d+):(\d+);', content)
        if proxy_pass_match:
            target_ip = proxy_pass_match.group(1)
            target_port = proxy_pass_match.group(2)
        else:
            target_ip = 'N/A'
            target_port = 'N/A'
        
        # Check SSL
        ssl_match = re.search(r'listen\s+443', content)
        has_ssl = bool(ssl_match)
        
        configs.append({
            'domain': server_name,
            'target': f'{target_ip}:{target_port}',
            'enabled': is_enabled,
            'ssl': has_ssl
        })

print(json.dumps(configs, indent=2))
"@

try {
    $result = python "$PSScriptRoot\_temp_ssh.py" $VPS_IP $VPS_USER $VPS_PASSWORD "python3 << 'PYEOF'
$command
PYEOF
"
    
    $configs = $result | ConvertFrom-Json
    
    Write-Host "`nConfigured Port Forwards:" -ForegroundColor Green
    Write-Host ("=" * 80)
    Write-Host ("{0,-30} {1,-25} {2,-10} {3,-10}" -f "Domain", "Target", "Enabled", "SSL") -ForegroundColor Yellow
    Write-Host ("=" * 80)
    
    foreach ($config in $configs) {
        $enabled = if ($config.enabled) { "Yes" } else { "No" }
        $ssl = if ($config.ssl) { "Yes" } else { "No" }
        Write-Host ("{0,-30} {1,-25} {2,-10} {3,-10}" -f $config.domain, $config.target, $enabled, $ssl)
    }
    
    Write-Host ("=" * 80)
    Write-Host "Total: $($configs.Count) forwards" -ForegroundColor Cyan
} catch {
    Write-Error "Failed to list forwards: $_"
    exit 1
}
