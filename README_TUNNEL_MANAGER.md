# Tunnel Manager - Web Interface

Blazor .NET 10 web application for managing WireGuard tunnels, nginx HTTP forwards, SSH tunnels, and traffic statistics.

## Features

- **HTTP Forward Management**: Create, edit, delete nginx HTTP forwards with SSL and WebSocket support
- **Basic Auth Management**: Add/remove HTTP Basic Authentication per forward
- **SSH Tunnel Management**: Create external SSH access tunnels to internal VMs via nginx stream module
- **Traffic Statistics**: Collect and view traffic stats per domain (requests, bytes, status codes)
- **WireGuard Status**: Monitor WireGuard tunnel status and peers
- **Dashboard**: Overview of all tunnels, forwards, and system status

## Architecture

```
Browser → DO VPS (nginx) → WireGuard → LXC (192.168.66.154:5100) → SSH → DO VPS (nginx configs)
```

## Authentication

- Username: `vjmap`
- Password: `expmap010687map`

Configured in `appsettings.json`.

## Deployment

See [README_DEPLOY.md](README_DEPLOY.md) for detailed deployment instructions.

Quick start:
1. Run `.\deploy\setup-service.bat` (one-time)
2. Run `.\deploy\deploy.bat` to deploy
3. Add forward: `.\add-forward.ps1 -Domain "tunnel.denys.fast" -Target "192.168.66.154:5100" -GetSSL`
4. Add DNS: `tunnel` A-record → `159.223.76.215` (Proxy OFF)

## Usage

### HTTP Forwards

1. Navigate to **HTTP Forwards** page
2. Click **Add Forward**
3. Enter domain (e.g., `example.denys.fast`)
4. Enter target (e.g., `192.168.66.142:5000`)
5. Enable WebSocket if needed
6. Enable SSL to request certificate automatically

### SSH Tunnels

1. Navigate to **SSH Tunnels** page
2. Click **Add SSH Tunnel**
3. Enter name (e.g., `myvm`)
4. Enter target IP (e.g., `192.168.66.160`)
5. Target port (default: 22)
6. External port (auto-assigned or manual, range: 2222-2299)

External users can then connect:
```bash
ssh user@159.223.76.215 -p 2223
```

### Basic Auth

1. Go to **HTTP Forwards** page
2. Click the security icon on a forward
3. Enter username and password
4. Click **Add Auth** or **Update Password**

## Configuration

All VPS settings in `appsettings.json`:

```json
{
  "Vps": {
    "Host": "159.223.76.215",
    "User": "root",
    "Password": "...",
    "NginxConfigDir": "/etc/nginx/sites-available",
    "NginxEnabledDir": "/etc/nginx/sites-enabled",
    "NginxStreamDir": "/etc/nginx/stream.d",
    "SshTunnelPortRange": {
      "Min": 2222,
      "Max": 2299
    }
  }
}
```

## Tech Stack

- .NET 10
- Blazor Server (InteractiveServer)
- MudBlazor 6.11
- SSH.NET (Renci.SshNet)
- Entity Framework Core + SQLite

## Project Structure

```
src/TunnelManager.Web/
├── Components/
│   ├── Pages/
│   │   ├── Dashboard/Index.razor
│   │   ├── Forwards/ForwardList.razor, ForwardEdit.razor, AuthManage.razor
│   │   ├── SshTunnels/SshTunnelList.razor, SshTunnelEdit.razor
│   │   └── Stats/TrafficStats.razor
│   └── Layout/MainLayout.razor, NavMenu.razor
├── Services/
│   ├── SshService.cs
│   ├── NginxService.cs
│   ├── NginxAuthService.cs
│   ├── SshTunnelService.cs
│   ├── WireGuardService.cs
│   └── StatsCollectorService.cs
├── Models/
│   ├── ForwardConfig.cs
│   ├── SshTunnelConfig.cs
│   └── TrafficStats.cs
└── Data/AppDbContext.cs
```

## License

Private project.
