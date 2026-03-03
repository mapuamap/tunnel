@echo off
setlocal EnableDelayedExpansion
chcp 65001 >nul

:: ============================================
:: SETTINGS
:: ============================================
set VM_HOST=192.168.66.154
set VM_USER=expmap
set VM_PROJECT_PATH=/home/expmap/docker/opt/stacks/tunnelmanager
set GIT_REPO=github.com/mapuamap/tunnel.git
set APPSETTINGS_PATH=src\TunnelManager.Web\appsettings.json
set APP_PORT=5101

:: Navigate to project root (one level up from deploy folder)
cd /d "%~dp0.."

:: Check for GIT_TOKEN environment variable
if "%GIT_TOKEN%"=="" (
    echo [ERROR] GIT_TOKEN environment variable is not set!
    echo Please set it: set GIT_TOKEN=your_token_here
    echo Or see deploy\SETUP_GIT_TOKEN.md for instructions
    pause
    exit /b 1
)

echo.
echo ==========================================
echo      Tunnel Manager Deploy Script
echo ==========================================
echo.

:: ============================================
:: 0. INCREMENT VERSION
:: ============================================
echo [0/6] Incrementing version...
powershell -NoProfile -Command "try { $path = '%APPSETTINGS_PATH%'; if (-not (Test-Path $path)) { throw 'File not found' }; $json = Get-Content $path -Raw | ConvertFrom-Json; if (-not $json.AppConfig) { $json | Add-Member -Type NoteProperty -Name 'AppConfig' -Value @{} -Force }; if (-not $json.AppConfig.Version) { $json.AppConfig.Version = '1.00' }; $ver = $json.AppConfig.Version; if ($ver -match '^[\d.]+$') { $newVer = ([decimal]$ver + 0.01).ToString('F2') } else { $newVer = $ver + '+' }; $json.AppConfig.Version = $newVer; $json | ConvertTo-Json -Depth 100 | Set-Content $path -Encoding UTF8; Write-Host \"Version: $ver -> $newVer\" } catch { Write-Host \"Warning: $($_.Exception.Message)\" }"
if errorlevel 1 (
    echo [!] Warning: Could not increment version
)

:: ============================================
:: 1. BUILD
:: ============================================
echo [1/6] Building project...
pushd src\TunnelManager.Web
dotnet build -c Release
if errorlevel 1 (
    echo [X] Build failed!
    popd
    pause
    exit /b 1
)
popd
echo [OK] Build successful

:: ============================================
:: 2. GIT ADD
:: ============================================
echo [2/6] Adding files to Git...
git add -A

:: ============================================
:: 3. CHECK FOR CHANGES
:: ============================================
echo [3/6] Checking for changes...

git diff --cached --quiet
if not errorlevel 1 (
    echo [!] No changes to commit - skipping to deploy...
    goto :DEPLOY
)

:: ============================================
:: 4. GIT COMMIT (auto message)
:: ============================================
echo [4/6] Creating commit...

:: Get changed files count
for /f %%i in ('git diff --cached --numstat ^| find /c /v ""') do set FILE_COUNT=%%i

:: Get list of modified services/components (unique)
set SERVICES=
for /f "tokens=*" %%f in ('git diff --cached --name-only ^| findstr /i "Service Component"') do (
    for %%n in (%%~nf) do (
        echo !SERVICES! | findstr /i "%%n" >nul || set SERVICES=!SERVICES! %%n
    )
)

:: Generate locale-independent timestamp via PowerShell
for /f "tokens=*" %%t in ('powershell -NoProfile -Command "Get-Date -Format \"yyyy-MM-dd HH:mm\""') do set TIMESTAMP=%%t

if "!SERVICES!"=="" (
    set COMMIT_MSG=deploy: update %FILE_COUNT% files [%TIMESTAMP%]
) else (
    set COMMIT_MSG=deploy: update!SERVICES! [%TIMESTAMP%]
)

echo     Message: %COMMIT_MSG%
git commit -m "%COMMIT_MSG%"

:: ============================================
:: 5. GIT PUSH
:: ============================================
echo [5/6] Pushing to GitHub...
git push -u origin master
if errorlevel 1 (
    echo [WARNING] Push failed
    echo [INFO] Continuing with deployment anyway...
) else (
    echo [OK] Code pushed to GitHub
)

:DEPLOY
:: ============================================
:: 6. UPDATE ON VM (Docker)
:: ============================================
echo.
echo [6/6] Updating Tunnel Manager on VM...
echo     Connecting to %VM_USER%@%VM_HOST%
echo     Project path: %VM_PROJECT_PATH%
echo.

ssh -t %VM_USER%@%VM_HOST% "cd %VM_PROJECT_PATH% && git fetch https://%GIT_TOKEN%@%GIT_REPO% master && git reset --hard FETCH_HEAD && docker compose -f docker-compose.prod.yml down && docker compose -f docker-compose.prod.yml up -d --build"

if errorlevel 1 (
    echo.
    echo [X] Error updating VM
    echo.
    echo Try manually:
    echo   ssh %VM_USER%@%VM_HOST%
    echo   cd %VM_PROJECT_PATH%
    echo   git fetch https://TOKEN@%GIT_REPO% master
    echo   git reset --hard FETCH_HEAD
    echo   docker compose -f docker-compose.prod.yml down
    echo   docker compose -f docker-compose.prod.yml up -d --build
    pause
    exit /b 1
)

:: ============================================
:: DONE
:: ============================================
echo.
echo ==========================================
echo   [OK] Deploy completed successfully!
echo ==========================================
echo.
echo Tunnel Manager restarted on VM
echo Port: %APP_PORT%
echo.
pause
