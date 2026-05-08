@echo off
setlocal

echo [Unity Compile] Checking Unity Editor path...

REM Attempt to find Unity 2022 installation
set "UNITY_HUB=D:/Program Files/Unity/Hub/Editor"
set "UNITY_VERSION=2022"
set "PROJECT_PATH=D:/unity_project/Bocchi"

REM Try to find the Unity Editor executable
for /d %%V in ("%UNITY_HUB%\%UNITY_VERSION%*") do (
    if exist "%%V\Editor\Unity.exe" (
        set "UNITY_EXE=%%V\Editor\Unity.exe"
        goto :found
    )
)

echo Unity 2022 not found at %UNITY_HUB%\%UNITY_VERSION%*
echo Please update compile.bat with the correct Unity path.
pause
exit /b 1

:found
echo Found Unity at: %UNITY_EXE%
echo Project: %PROJECT_PATH%
echo.

echo Starting Unity compilation (headless batch mode)...
"%UNITY_EXE%" ^
    -projectPath "%PROJECT_PATH%" ^
    -batchmode ^
    -nographics ^
    -quit ^
    -executeMethod UnityEditor.SyncVS.SyncIfFirstFileOpen ^
    -logFile "%PROJECT_PATH%\.claude\agents\code-agent\codeScripts\compile_output.log"

if errorlevel 1 (
    echo.
    echo Unity Build FAILED. Check compile_output.log for details.
    type "%PROJECT_PATH%\.claude\agents\code-agent\codeScripts\compile_output.log" 2>nul
    pause
) else (
    echo.
    echo Unity Build SUCCEEDED.
)

endlocal
