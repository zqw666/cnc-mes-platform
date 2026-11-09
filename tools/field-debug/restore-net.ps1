# 删除现场调试 IP，恢复网卡原状
param(
    [string]$IP = '192.168.125.210'
)

# ---- 自动提权 ----
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "需要管理员权限，正在申请提权..." -ForegroundColor Yellow
    Start-Process powershell.exe -Verb RunAs -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass',
        '-File', "`"$PSCommandPath`"",
        '-IP', $IP
    )
    exit
}

$found = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue | Where-Object { $_.IPAddress -eq $IP }

if (-not $found) {
    Write-Host "未找到地址 $IP，无需还原。" -ForegroundColor Cyan
    Read-Host "按回车键退出"
    exit 0
}

Write-Host "将要删除以下地址配置：" -ForegroundColor Yellow
$found | Format-Table InterfaceAlias, IPAddress, PrefixLength -AutoSize

$ans = Read-Host "确认删除? (y/n)"
if ($ans -ne 'y' -and $ans -ne 'Y') {
    Write-Host "已取消。" -ForegroundColor Red
    exit 1
}

$found | ForEach-Object {
    Remove-NetIPAddress -InterfaceIndex $_.InterfaceIndex -IPAddress $_.IPAddress -Confirm:$false -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "已还原。" -ForegroundColor Green
Write-Host "提示：如果该网卡原本有静态 IP，需要用 GUI 重新填回。" -ForegroundColor Cyan
Write-Host ""
Read-Host "按回车键退出"
