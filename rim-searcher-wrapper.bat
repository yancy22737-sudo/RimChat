@echo off
setlocal

set RIMSEARCHER_DIR=E:\SteamLibrary\steamapps\common\RimWorld\RimSearcher
set SERVER_EXE=%RIMSEARCHER_DIR%\RimSearcher.server_2.exe
set CONFIG_FILE=%RIMSEARCHER_DIR%\config.json
set WRAPPER_PS1=c:\Users\Administrator\source\repos\RimChat\rim-searcher-wrapper.ps1

if not "%1"=="" goto handle_arg

goto mcp_mode

:handle_arg
if /i "%1"=="start" goto do_start
if /i "%1"=="stop" goto do_stop
if /i "%1"=="restart" goto do_restart
if /i "%1"=="status" goto do_status
goto usage

:do_start
powershell -NoProfile -ExecutionPolicy Bypass -File "%WRAPPER_PS1%" -Action start
goto end

:do_stop
powershell -NoProfile -ExecutionPolicy Bypass -File "%WRAPPER_PS1%" -Action stop
goto end

:do_restart
powershell -NoProfile -ExecutionPolicy Bypass -File "%WRAPPER_PS1%" -Action restart
goto end

:do_status
powershell -NoProfile -ExecutionPolicy Bypass -File "%WRAPPER_PS1%" -Action status
goto end

:mcp_mode
tasklist /FI "IMAGENAME eq RimSearcher.server_2.exe" 2>NUL | find /I "RimSearcher.server_2.exe" >NUL
if errorlevel 1 goto start_service
goto launch_server

:start_service
echo [%DATE% %TIME%] Starting RimSearcher service... >> "%RIMSEARCHER_DIR%\wrapper.log"
set RIMSEARCHER_CONFIG=%CONFIG_FILE%
start "" /B "%SERVER_EXE%"

set RETRY=0
:check_loop
timeout /t 1 /nobreak >NUL
set /a RETRY+=1
tasklist /FI "IMAGENAME eq RimSearcher.server_2.exe" 2>NUL | find /I "RimSearcher.server_2.exe" >NUL
if errorlevel 1 goto check_next
goto launch_server

:check_next
if %RETRY% LSS 5 goto check_loop
echo [%DATE% %TIME%] ERROR: Failed to start RimSearcher service >> "%RIMSEARCHER_DIR%\wrapper.log"
exit /b 1

:launch_server
set RIMSEARCHER_CONFIG=%CONFIG_FILE%
"%SERVER_EXE%"
goto end

:usage
echo Usage: %~nx0 [start^|stop^|restart^|status]
echo   start   - Start the RimSearcher service
echo   stop    - Stop the RimSearcher service
echo   restart - Restart the RimSearcher service
echo   status  - Show service status
echo   (no args) - MCP mode: ensure service running and launch
exit /b 1

:end
endlocal
