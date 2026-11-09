# 给指定网卡配置现场调试 IP（默认 192.168.125.210/24，不设网关，不影响原有上网）
param(
    [string]$IP = '192.168.125.210',
    [int]$Prefix = 24
)

# ---- 自动提权 ----
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "需要管理员权限，正在申请提权..." -ForegroundColor Yellow
    Start-Process powershell.exe -Verb RunAs -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass',
        '-File', "`"$PSCommandPath`"",
        '-IP', $IP, '-Prefix', $Prefix
    )
    exit
}

Write-Host ""
Write-Host "============ 当前网卡列表 ============" -ForegroundColor Cyan
Get-NetAdapter | Sort-Object ifIndex |
    Format-Table ifIndex, Name, InterfaceDescription, Status, LinkSpeed -AutoSize

Write-Host "请选择要接入设备网的网卡 ifIndex" -ForegroundColor Yellow
Write-Host "（建议选 USB 网口，不要选 WLAN）" -ForegroundColor Yellow
$idx = Read-Host "ifIndex"

if ([string]::IsNullOrWhiteSpace($idx)) {
    Write-Host "未输入，已取消。" -ForegroundColor Red
    exit 1
}

$idxInt = 0
if (-not [int]::TryParse($idx.Trim(), [ref]$idxInt)) {
    Write-Host "输入的不是数字，已取消。" -ForegroundColor Red
    exit 1
}

$adapter = Get-NetAdapter -InterfaceIndex $idxInt -ErrorAction SilentlyContinue
if (-not $adapter) {
    Write-Host "找不到该网卡，已取消。" -ForegroundColor Red
    exit 1
}

if ($adapter.Status -ne 'Up') {
    Write-Host ("警告：网卡 [{0}] 当前状态为 {1}，请确认网线已经插好。" -f $adapter.Name, $adapter.Status) -ForegroundColor Yellow
}

$existing = Get-NetIPAddress -InterfaceIndex $idxInt -AddressFamily IPv4 -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host ""
    Write-Host "该网卡现有 IPv4 地址（配置新地址前会被清除）：" -ForegroundColor Yellow
    $existing | Format-Table InterfaceAlias, IPAddress, PrefixLength -AutoSize
    $ans = Read-Host "确认继续? (y/n)"
    if ($ans -ne 'y' -and $ans -ne 'Y') {
        Write-Host "已取消。" -ForegroundColor Red
        exit 1
    }
    $existing | ForEach-Object {
        Remove-NetIPAddress -InterfaceIndex $_.InterfaceIndex -IPAddress $_.IPAddress -Confirm:$false -ErrorAction SilentlyContinue
    }
}

New-NetIPAddress -InterfaceIndex $idxInt -IPAddress $IP -PrefixLength $Prefix | Out-Null

Write-Host ""
Write-Host ("已为网卡 [{0}] 配置 {1}/{2}（未设网关，默认路由不受影响）" -f $adapter.Name, $IP, $Prefix) -ForegroundColor Green
Get-NetIPAddress -InterfaceIndex $idxInt -AddressFamily IPv4 |
    Format-Table InterfaceAlias, IPAddress, PrefixLength -AutoSize

Write-Host "下一步：运行 .\check-net.ps1 验证连通性" -ForegroundColor Cyan
Write-Host ""
Read-Host "按回车键退出"
