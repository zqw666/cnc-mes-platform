# 测试目标主机的关键端口是否开放
param(
    [string]$Target = '192.168.125.201'
)

Write-Host ""
Write-Host "============ 端口连通性测试 ============" -ForegroundColor Cyan
Write-Host "目标主机：$Target" -ForegroundColor Cyan
Write-Host ""

$ports = @(
    @{ p = 8888; desc = '后端 HTTP' },
    @{ p = 3389; desc = '远程桌面' },
    @{ p = 4026; desc = 'VS 远程调试' },
    @{ p = 445;  desc = 'SMB 共享' },
    @{ p = 1433; desc = 'SQL Server' }
)

foreach ($item in $ports) {
    $ok = Test-NetConnection -ComputerName $Target -Port $item.p -InformationLevel Quiet -WarningAction SilentlyContinue
    $txt = if ($ok) { '开放' } else { '不通' }
    $color = if ($ok) { 'Green' } else { 'Red' }
    Write-Host ("  {0,-6} {1,-16} {2}" -f $item.p, $item.desc, $txt) -ForegroundColor $color
}

Write-Host ""
Write-Host "提示：PLC 的 9600 是 UDP 端口，上面测不了，用 check-net.ps1 的 ping 结果判断即可。" -ForegroundColor Cyan
Write-Host ""
Read-Host "按回车键退出"
