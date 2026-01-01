@echo off
echo ====================================
echo       IrisSort - Starting...
echo ====================================
echo.

echo Checking LM Studio connection...
echo Endpoint: http://127.0.0.1:1234
echo Model: glm-4.6v-flash
echo.

REM Simple check if LM Studio is running (curl the models endpoint)
curl -s http://127.0.0.1:1234/v1/models > nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo WARNING: Cannot reach LM Studio at http://127.0.0.1:1234
    echo.
    echo Please ensure:
    echo   1. LM Studio is running
    echo   2. Local server is started
    echo   3. A vision model is loaded (e.g., glm-4.6v-flash^)
    echo.
) else (
    echo LM Studio: Connected
    echo.
)

echo Building IrisSort...
dotnet build --nologo --verbosity quiet

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ERROR: Build failed!
    pause
    exit /b 1
)

echo Build successful!
echo.
echo Launching IrisSort...
echo.

start "" "src\IrisSort.Desktop\IrisSort.Desktop\bin\Debug\net8.0-windows\IrisSort.Desktop.exe"

echo IrisSort is running!
echo You can close this window.
echo.
