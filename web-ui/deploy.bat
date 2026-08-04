@echo off
REM deploy.bat - sync frontend files to bin
echo === Deploy frontend to bin ===
echo.
copy /y "D:\桌面\erp\前后端分离\web-ui\frontend\index.html" "D:\桌面\erp\前后端分离\web-ui\backend\bin\Any CPU\Debug\net472\index.html" >nul
copy /y "D:\桌面\erp\前后端分离\web-ui\frontend\script.js" "D:\桌面\erp\前后端分离\web-ui\backend\bin\Any CPU\Debug\net472\script.js" >nul
copy /y "D:\桌面\erp\前后端分离\web-ui\frontend\styles.css" "D:\桌面\erp\前后端分离\web-ui\backend\bin\Any CPU\Debug\net472\styles.css" >nul
copy /y "D:\桌面\erp\前后端分离\web-ui\frontend\格力.png" "D:\桌面\erp\前后端分离\web-ui\backend\bin\Any CPU\Debug\net472\格力.png" >nul
copy /y "D:\桌面\erp\前后端分离\web-ui\frontend\机器人.png" "D:\桌面\erp\前后端分离\web-ui\backend\bin\Any CPU\Debug\net472\机器人.png" >nul
copy /y "D:\桌面\erp\前后端分离\web-ui\frontend\料架.png" "D:\桌面\erp\前后端分离\web-ui\backend\bin\Any CPU\Debug\net472\料架.png" >nul
copy /y "D:\桌面\erp\前后端分离\web-ui\frontend\罗德斯.png" "D:\桌面\erp\前后端分离\web-ui\backend\bin\Any CPU\Debug\net472\罗德斯.png" >nul
echo.
echo All files deployed.