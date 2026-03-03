# Temporary SSH connection script
param(
    [string]$TargetHost,
    [string]$User = "root",
    [string]$Password,
    [string]$Command
)

# Install Posh-SSH if not available
if (-not (Get-Module -ListAvailable -Name Posh-SSH)) {
    Write-Host "Installing Posh-SSH module..."
    Install-Module -Name Posh-SSH -Force -Scope CurrentUser -SkipPublisherCheck
}

Import-Module Posh-SSH

$securePassword = ConvertTo-SecureString $Password -AsPlainText -Force
$credential = New-Object System.Management.Automation.PSCredential($User, $securePassword)

try {
    $session = New-SSHSession -ComputerName $TargetHost -Credential $credential -AcceptKey -ErrorAction Stop
    if ($Command) {
        $result = Invoke-SSHCommand -SessionId $session.SessionId -Command $Command
        Write-Output $result.Output
        if ($result.ExitStatus -ne 0) {
            Write-Error "Command failed with exit code: $($result.ExitStatus)"
            Write-Output $result.Error
            exit $result.ExitStatus
        }
    }
    Remove-SSHSession -SessionId $session.SessionId | Out-Null
    return $session
} catch {
    Write-Error "SSH connection failed: $_"
    exit 1
}
