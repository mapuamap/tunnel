@echo off
chcp 65001 >nul
setlocal EnableDelayedExpansion

:: ===== CONFIGURATION =====
set VM_HOST=192.168.66.154
set VM_USER=expmap
set VM_PROJECT_PATH=/home/expmap/tunnelmanager
set SERVICE_NAME=tunnelmanager
set APP_PORT=5100

echo.
echo ========================================
echo  Tunnel Manager Service Setup
echo ========================================
echo.

echo [SETUP] Creating systemd service on %VM_HOST%...
echo.

setlocal DisableDelayedExpansion

ssh %VM_USER%@%VM_HOST% "sudo tee /etc/systemd/system/%SERVICE_NAME%.service > /dev/null << 'EOF'
[Unit]
Description=Tunnel Manager Web Application
After=network.target

[Service]
Type=notify
User=expmap
WorkingDirectory=%VM_PROJECT_PATH%/publish
ExecStart=/usr/bin/dotnet %VM_PROJECT_PATH%/publish/TunnelManager.Web.dll
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=%SERVICE_NAME%
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://0.0.0.0:%APP_PORT%

[Install]
WantedBy=multi-user.target
EOF
sudo systemctl daemon-reload && sudo systemctl enable %SERVICE_NAME% && sudo systemctl start %SERVICE_NAME% && echo [OK] Service created and started"

endlocal

if errorlevel 1 (
    echo [ERROR] Service setup failed!
    pause
    exit /b 1
)

echo.
echo ========================================
echo  SERVICE SETUP COMPLETE!
echo ========================================
echo.
echo  Service: %SERVICE_NAME%
echo  Status: Check with 'systemctl status %SERVICE_NAME%'
echo.
echo ========================================

pause
exit /b 0
