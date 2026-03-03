# Add Basic HTTP Authentication to a domain
param(
    [Parameter(Mandatory=$true)]
    [string]$Domain,
    
    [Parameter(Mandatory=$true)]
    [string]$Username,
    
    [Parameter(Mandatory=$true)]
    [string]$Password,
    
    [string]$Realm = "Restricted Access"
)

. "$PSScriptRoot\config.ps1"

Write-Host "Adding Basic Auth to: $Domain" -ForegroundColor Cyan
Write-Host "Username: $Username" -ForegroundColor Cyan
Write-Host "Realm: $Realm" -ForegroundColor Cyan

# Validate domain format
if ($Domain -notmatch '^[a-zA-Z0-9][a-zA-Z0-9\.-]+[a-zA-Z0-9]$') {
    Write-Error "Invalid domain format: $Domain"
    exit 1
}

# Check if domain configuration exists
Write-Host "Checking if domain configuration exists..." -ForegroundColor Yellow
$checkResult = python "$PSScriptRoot\_temp_ssh.py" $VPS_IP $VPS_USER $VPS_PASSWORD "test -f /etc/nginx/sites-available/$Domain && echo 'EXISTS' || echo 'NOT_FOUND'"

if ($checkResult -notmatch 'EXISTS') {
    Write-Error "Domain configuration not found: $Domain"
    Write-Host "Use add-forward.ps1 to create the domain first" -ForegroundColor Yellow
    exit 1
}

# Add authentication
$result = python "$PSScriptRoot\_add_auth.py" $VPS_IP $VPS_USER $VPS_PASSWORD $Domain $Username $Password $Realm

if ($LASTEXITCODE -eq 0) {
    Write-Host "`nOK: Basic Auth successfully added to $Domain" -ForegroundColor Green
    Write-Host "`nAccess credentials:" -ForegroundColor Cyan
    Write-Host "  Domain: https://$Domain" -ForegroundColor White
    Write-Host "  Username: $Username" -ForegroundColor White
    Write-Host "  Password: $Password" -ForegroundColor White
    Write-Host "`nNote: Save these credentials securely!" -ForegroundColor Yellow
} else {
    Write-Error "Failed to add Basic Auth: $result"
    exit 1
}
