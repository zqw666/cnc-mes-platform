@echo off
chcp 65001 >nul
cd /d "%~dp0"
git -c credential.helper= push origin main
pause
