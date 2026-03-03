@echo off
setlocal EnableDelayedExpansion

:: ============================================
:: SETTINGS
:: ============================================
set VM_HOST=192.168.66.154
set VM_USER=expmap
set VM_PROJECT_PATH=/home/expmap/docker/opt/stacks/bot
set GIT_TOKEN=YOUR_GITHUB_TOKEN_HERE
set GIT_REPO=github.com/mapuamap/denysai-bot.git
set APPSETTINGS_PATH=src\TelegramBot.Web\appsettings.json

echo.
echo ==========================================
echo          DenysAI Deploy Script
echo ==========================================
echo.

:: ============================================
:: 0a. GENERATE CODE STATS
:: ============================================
echo [0/7] Generating code statistics...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0generate-code-stats.ps1"
if errorlevel 1 (
    echo [!] Warning: Could not generate code stats
) else (
    echo [OK] Code stats saved
)

:: ============================================
:: 0b. INCREMENT VERSION
:: ============================================
echo [1/7] Incrementing version...
powershell -NoProfile -Command "try { $path = '%APPSETTINGS_PATH%'; if (-not (Test-Path $path)) { throw 'File not found' }; $json = Get-Content $path -Raw | ConvertFrom-Json; $ver = $json.AppVersion; if ($ver -match '^[\d.]+$') { $newVer = ([decimal]$ver + 0.01).ToString('F2') } else { $newVer = $ver + '+' }; $json.AppVersion = $newVer; $json | ConvertTo-Json -Depth 100 | Set-Content $path -Encoding UTF8; Write-Host \"Version: $ver -> $newVer\" } catch { Write-Host \"Warning: $($_.Exception.Message)\" }"
if errorlevel 1 (
    echo [!] Warning: Could not increment version
)

:: ============================================
:: 2. GIT ADD
:: ============================================
echo [2/7] Adding files to Git...
git add -A

:: ============================================
:: 3. CHECK FOR CHANGES
:: ============================================
echo [3/7] Checking for changes...

git diff --cached --quiet
if not errorlevel 1 (
    echo [!] No changes to commit - skipping...
    goto :DEPLOY
)

:: ============================================
:: 4. GIT COMMIT (auto message)
:: ============================================
echo [4/7] Creating commit...

:: Get changed files count
for /f %%i in ('git diff --cached --numstat ^| find /c /v ""') do set FILE_COUNT=%%i

:: Get short summary of changes
for /f "tokens=*" %%a in ('git diff --cached --stat ^| findstr /R "insertion deletion"') do set STAT_LINE=%%a

:: Get list of modified services/handlers (unique)
set SERVICES=
for /f "tokens=*" %%f in ('git diff --cached --name-only ^| findstr /i "Service Handler"') do (
    for %%n in (%%~nf) do (
        echo !SERVICES! | findstr /i "%%n" >nul || set SERVICES=!SERVICES! %%n
    )
)

:: Generate commit message
set TIMESTAMP=%date:~-4%-%date:~3,2%-%date:~0,2% %time:~0,5%
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
echo [5/7] Pushing to GitHub...
git push
if errorlevel 1 (
    echo [X] Error pushing to GitHub
    echo Check that token is configured: git config --global credential.helper manager
    pause
    exit /b 1
)

echo [OK] Code pushed to GitHub

:DEPLOY
:: ============================================
:: 6. UPDATE ON VM
:: ============================================
echo.
echo [6/7] Updating bot on VM...
echo     Connecting to %VM_USER%@%VM_HOST%
echo     Enter password when prompted
echo.

ssh -t %VM_USER%@%VM_HOST% "cd %VM_PROJECT_PATH% && git fetch https://%GIT_TOKEN%@%GIT_REPO% main && git reset --hard FETCH_HEAD && docker compose -f docker-compose.prod.yml down && docker compose -f docker-compose.prod.yml up -d --build"

if errorlevel 1 (
    echo.
    echo [X] Error updating VM
    echo.
    echo Try manually:
    echo   ssh %VM_USER%@%VM_HOST%
    echo   cd %VM_PROJECT_PATH%
    echo   git pull
    echo   docker compose down
    echo   docker compose up -d --build
    pause
    exit /b 1
)

:: ============================================
:: DONE
:: ============================================
echo.
echo ==========================================
echo      [OK] Deploy completed successfully!
echo ==========================================
echo.
echo Bot restarted on VM
echo.

pause
