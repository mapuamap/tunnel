# Tunnel Manager - Deployment Instructions

## Prerequisites

1. Git repository created: `github.com/mapuamap/tunnel`
2. VM at `192.168.66.154` with user `expmap`
3. .NET 10 SDK installed on VM at `/root/.dotnet/dotnet`

## Initial Setup (One-time)

### 1. Setup systemd service on VM

Run from Windows:
```powershell
.\deploy\setup-service.bat
```

This will:
- Create systemd service file at `/etc/systemd/system/tunnelmanager.service`
- Enable and start the service
- Service runs on port 5100

### 2. Add nginx forward for tunnel.denys.fast

After the service is running, add the forward:

```powershell
.\add-forward.ps1 -Domain "tunnel.denys.fast" -Target "192.168.66.154:5100" -GetSSL
```

### 3. Add DNS record in Cloudflare

Add A-record:
- Name: `tunnel`
- Type: `A`
- Value: `159.223.76.215`
- Proxy: **OFF** (gray cloud)

## Deployment

### Regular deployment

```powershell
.\deploy\deploy.bat
```

Or with custom commit message:
```powershell
.\deploy\deploy.bat "fix: update stats collection"
```

### What deploy.bat does:

1. Builds the project locally
2. Commits and pushes to GitHub
3. SSH to VM and:
   - Pulls latest code
   - Publishes the app
   - Restarts the service

## Access

After deployment and DNS setup:
- Web UI: `https://tunnel.denys.fast`
- Login: `vjmap`
- Password: `expmap010687map`

## Troubleshooting

### Service not starting

SSH to VM and check:
```bash
sudo systemctl status tunnelmanager
sudo journalctl -u tunnelmanager -f
```

### Port already in use

Change port in `appsettings.json` and `deploy.bat`:
- `APP_PORT=5100` → `APP_PORT=5101`

### Build errors

Clean and rebuild:
```powershell
cd src\TunnelManager.Web
dotnet clean
dotnet build
```
