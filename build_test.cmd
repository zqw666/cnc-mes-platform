@echo off
chcp 65001 >nul
"C:\Program Files\dotnet\dotnet.exe" msbuild "D:\桌面\erp\前后端分离\web-ui\unload_machine\unload_machine\unload_machine.csproj" /p:Configuration=Debug /t:Build /v:m /nologo /p:BuildProjectReferences=false
echo Exit code: %ERRORLEVEL%
pause
