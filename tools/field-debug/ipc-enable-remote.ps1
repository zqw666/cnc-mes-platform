# 在【工控机】上执行：开启后端 8888 的远程访问
# 原理：程序只监听 127.0.0.1，这里用 portproxy 在外面套一层转发，程序代码无需改动
param(
    [int]$Port = 8888
)

# ---- 自动提权 ----
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "需要管理员权限，正在申请提权..." -ForegroundColor Yellow
    Start-Process powershell.exe -Verb RunAs -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass',
        '-File', "`"$PSCommandPath`"",
        '-Port', $Port
    )
    exit
}

Write-Host ""
Write-Host "============ 开启工控机远程访问 ============" -ForegroundColor Cyan
Write-Host ""

Write-Host "[1/4] 启动 IP Helper 服务（portproxy 依赖此服务）" -ForegroundColor Yellow
Set-Service -Name iphlpsvc -StartupType Automatic -ErrorAction SilentlyContinue
Start-Service -Name iphlpsvc -ErrorAction SilentlyContinue

Write-Host "[2/4] 建立端口转发 0.0.0.0:$Port -> 127.0.0.1:$Port" -ForegroundColor Yellow
netsh interface portproxy delete v4tov4 listenaddress=0.0.0.0 listenport=$Port 2>&1 | Out-Null
netsh interface portproxy add v4tov4 listenaddress=0.0.0.0 listenport=$Port connectaddress=127.0.0.1 connectport=$Port 2>&1 | Out-Null

Write-Host "[3/4] 放行防火墙 TCP $Port" -ForegroundColor Yellow
Remove-NetFirewallRule -DisplayName "MachineMonitor-$Port" -ErrorAction SilentlyContinue
New-NetFirewallRule -DisplayName "MachineMonitor-$Port" -Direction Inbound -Action Allow -Protocol TCP -LocalPort $Port -ErrorAction SilentlyContinue | Out-Null

Write-Host "[4/4] 启用远程桌面（系统需专业版，家庭版无效）" -ForegroundColor Yellow
Set-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server' -Name fDenyTSConnections -Value 0 -ErrorAction SilentlyContinue
Get-NetFirewallRule -DisplayGroup 'Remote Desktop' -ErrorAction SilentlyContinue | Enable-NetFirewallRule -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "当前端口转发规则：" -ForegroundColor Cyan
netsh interface portproxy show v4tov4

Write-Host ""
Write-Host "完成。现在同网段其他机器可以访问 http://192.168.125.201:$Port/" -ForegroundColor Green
Write-Host ""
Read-Host "按回车键退出"
