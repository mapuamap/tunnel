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
set APP_PORT=5101

:: Navigate to project root (one level up from deploy folder)
cd /d "%~dp0.."

:: Check for GIT_TOKEN: env variable, or first argument
if not "%~1"=="" set GIT_TOKEN=%~1
if "%GIT_TOKEN%"=="" (
    echo [ERROR] GIT_TOKEN is not set!
    echo Usage:  deploy.bat ^<token^>
    echo    or:  set GIT_TOKEN=your_token  ^& deploy.bat
    exit /b 1
)

echo.
echo ==========================================
echo      Tunnel Manager Deploy Script
echo ==========================================
echo.

:: ============================================
:: 1. GIT ADD + COMMIT
:: ============================================
echo [1/3] Committing changes...
git add -A

git diff --cached --quiet
if not errorlevel 1 (
    echo [!] No changes to commit - skipping to deploy...
    goto :PUSH
)

:: Get changed files count
for /f %%i in ('git diff --cached --numstat ^| find /c /v ""') do set FILE_COUNT=%%i

:: Generate locale-independent timestamp
for /f "tokens=*" %%t in ('powershell -NoProfile -Command "Get-Date -Format \"yyyy-MM-dd HH:mm\""') do set TIMESTAMP=%%t

set COMMIT_MSG=deploy: update %FILE_COUNT% files [%TIMESTAMP%]

echo     Message: %COMMIT_MSG%
git commit -m "%COMMIT_MSG%"
if errorlevel 1 (
    echo [X] Commit failed!
    exit /b 1
)
echo [OK] Committed

:: ============================================
:: 2. GIT PUSH
:: ============================================
:PUSH
echo [2/3] Pushing to GitHub...
git push origin master
if errorlevel 1 (
    echo [X] Push failed!
    exit /b 1
)
echo [OK] Pushed to GitHub

:: ============================================
:: 3. BUILD + RUN ON VM
:: ============================================
echo.
echo [3/3] Building and starting on VM...
echo     Host: %VM_USER%@%VM_HOST%
echo     Path: %VM_PROJECT_PATH%
echo.

ssh %VM_USER%@%VM_HOST% "cd %VM_PROJECT_PATH% && git fetch https://%GIT_TOKEN%@%GIT_REPO% master && git reset --hard FETCH_HEAD && docker compose -f docker-compose.prod.yml down && docker compose -f docker-compose.prod.yml up -d --build"

if errorlevel 1 (
    echo.
    echo [X] Error building/starting on VM
    exit /b 1
)

:: ============================================
:: 4. VERIFY
:: ============================================
echo.
echo [4/4] Verifying deployment...
timeout /t 8 /nobreak >nul
ssh %VM_USER%@%VM_HOST% "docker ps --filter name=tunnelmanager --format '{{.Status}}'"

:: ============================================
:: DONE
:: ============================================
echo.
echo ==========================================
echo   [OK] Deploy completed successfully!
echo ==========================================
echo   App: http://%VM_HOST%:%APP_PORT%
echo.
