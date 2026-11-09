# 网络环境诊断（只读，不修改任何配置）
$ErrorActionPreference = 'SilentlyContinue'

Write-Host ""
Write-Host "============ 网络环境诊断 ============" -ForegroundColor Cyan
Write-Host ""

Write-Host "[1] 网卡与 IPv4 地址" -ForegroundColor Yellow
Get-NetIPAddress -AddressFamily IPv4 |
    Where-Object { $_.IPAddress -ne '127.0.0.1' } |
    Sort-Object InterfaceIndex |
    Format-Table InterfaceAlias, IPAddress, PrefixLength, InterfaceIndex -AutoSize

Write-Host "[2] 到 125 / 101 网段的路由" -ForegroundColor Yellow
$routes = Get-NetRoute -AddressFamily IPv4 | Where-Object { $_.DestinationPrefix -match '192\.168\.(125|101)\.' }
if ($routes) {
    $routes | Format-Table DestinationPrefix, NextHop, InterfaceAlias -AutoSize
} else {
    Write-Host "  (无相关路由，说明本机所在的网络里没有 125 / 101 网段)"
}

Write-Host "[3] 目标连通性" -ForegroundColor Yellow
$targets = @(
    @{ ip = '192.168.125.201'; desc = '工控机' },
    @{ ip = '192.168.101.10';  desc = 'PLC' },
    @{ ip = '192.168.101.51';  desc = '机床3' },
    @{ ip = '192.168.101.52';  desc = '机床2' },
    @{ ip = '192.168.101.53';  desc = '机床1' },
    @{ ip = '192.168.101.71';  desc = '扫码枪' }
)
foreach ($t in $targets) {
    $ok = Test-Connection -ComputerName $t.ip -Count 1 -Quiet
    $txt = if ($ok) { '可达' } else { '不可达' }
    $color = if ($ok) { 'Green' } else { 'Red' }
    Write-Host ("  {0,-18} {1,-8} {2}" -f $t.ip, $t.desc, $txt) -ForegroundColor $color
}

Write-Host ""
Write-Host "说明：" -ForegroundColor Cyan
Write-Host "  - 笔记本接在 125 段时，101 网段不可达属正常，两段之间需要工控机做 NAT 转发"
Write-Host "  - 工控机 8888 端口是否开放，请运行 test-ports.ps1"
Write-Host ""
Read-Host "按回车键退出"
