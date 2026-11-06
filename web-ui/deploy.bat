@echo off
REM deploy.bat - sync the whole frontend folder into the build output dir.
REM Equivalent to the CopyFrontend target in MachineMonitor.csproj, but runnable by hand.
REM
REM NOTE: paths are relative to this script (%~dp0). Do NOT hardcode absolute paths -
REM       the old version pointed at D:\...\erp\... which is a DIFFERENT copy of the project,
REM       so running it silently deployed to the wrong place.
setlocal EnableExtensions

set "ROOT=%~dp0"
set "SRC=%ROOT%frontend"
set "DST=%ROOT%backend\bin\Any CPU\Debug\net472"

if not exist "%SRC%" (
  echo [ERR] source not found: %SRC%
  exit /b 1
)
if not exist "%DST%" (
  echo [ERR] output dir not found: %DST%
  echo       Build the backend first, then re-run this script:
  echo         dotnet build "%ROOT%backend\MachineMonitor.csproj"
  exit /b 1
)

echo Source : %SRC%
echo Target : %DST%
echo.

REM /E  recurse incl. empty dirs      /XO only overwrite when source is newer
REM /XD skip bin obj .git            /XF skip editor backups
robocopy "%SRC%" "%DST%" /E /XO /XD bin obj .git /XF *.bak *.bak-* /NFL /NDL /NJH /NJS
set "RC=%ERRORLEVEL%"

REM robocopy exit codes: 0-7 = success, >=8 = real failure
if %RC% GEQ 8 (
  echo.
  echo [ERR] robocopy failed, exit code %RC%
  exit /b %RC%
)

echo.
echo Done. Frontend synced to output dir.
endlocal
