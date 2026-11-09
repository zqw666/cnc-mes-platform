<#
    ipc-setup.ps1
    【在工控机上运行】一次性开通远程访问入口。必须「以管理员身份运行」。

    做三件事：
      1. portproxy：把 0.0.0.0:8888 转发到 127.0.0.1:8888
         —— 后端只监听 localhost（Program.cs:24-27），这一步不改代码就能让外部访问控制页
      2. 防火墙放行 8888
      3. 可选：放行 ICMP(ping)；可选：开通远程桌面 3389

    用法：
        .\ipc-setup.ps1                    # 只做 8888 转发 + 防火墙（推荐，最不干扰生产）
        .\ipc-setup.ps1 -EnableRdp         # 额外开通远程桌面
        .\ipc-setup.ps1 -Status            # 只看当前状态，不改任何东西
        .\ipc-setup.ps1 -Undo              # 撤销本脚本做的全部改动

    风险提示：
      * 开通远程桌面(3389)会占用现场控制台画面，现场操作员会看到自己的屏幕被接管。
        生产运行中慎用；调试期间用 portproxy 开网页通常就够了。
      * portproxy 依赖系统服务 IP Helper(iphlpsvc)，本脚本会自动确保它启动。
#>
[CmdletBinding()]
param(
    [int]$WebPort = 8888,
    [switch]$EnableRdp,
    [switch]$Status,
    [switch]$Undo
)

$ErrorActionPreference = 'Continue'
function Pause-Exit { if (-not [Console]::IsInputRedirected) { [void](Read-Host) } }
function Pad-Wide {
    param([string]$s, [int]$w)
    $d = 0
    foreach ($ch in $s.ToCharArray()) {
        $c = [int][char]$ch
        if (($c -ge 0x2E80 -and $c -le 0xA4CF) -or ($c -ge 0xFF00 -and $c -le 0xFF60)) { $d += 2 } else { $d += 1 }
    }
    $p = $w - $d
    if ($p -lt 1) { $p = 1 }
    return $s + (' ' * $p)
}
$RuleName = "MM-Web-$WebPort"
$RdpRuleName = 'MM-RDP'

function Say { param([string]$M, [string]$C = 'Gray') Write-Host $M -ForegroundColor $C }

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

Say ('=' * 64) 'Cyan'
Say '  工控机远程访问入口配置' 'Cyan'
Say ('=' * 64) 'Cyan'
Say ("  主机名   : " + $env:COMPUTERNAME)
Say ("  管理员   : " + $(if ($isAdmin) { '是' } else { '否  <<< 必须右键「以管理员身份运行」' })) 'Yellow'
Say ''

# ---------------- 状态查看 ----------------
function Show-Status {
    Say '---- 本机 IPv4 ----' 'White'
    Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
        Where-Object { $_.IPAddress -ne '127.0.0.1' -and $_.IPAddress -notlike '169.254.*' } |
        Sort-Object InterfaceAlias |
        ForEach-Object { Say ("  {0} {1}/{2}" -f (Pad-Wide $_.InterfaceAlias 24), (Pad-Wide $_.IPAddress 18), $_.PrefixLength) }

    Say ''
    Say '---- portproxy 规则 ----' 'White'
    $pp = (netsh interface portproxy show v4tov4) 2>&1 | Out-String
    Say $pp

    Say '---- 服务监听 8888 ----' 'White'
    $ns = (netstat -ano | Select-String ":8888") 2>&1 | Out-String
    if ([string]::IsNullOrWhiteSpace($ns)) { Say '  (无进程监听 8888 —— MachineMonitor.exe 是否在运行？)' 'Yellow' }
    else { Say $ns }

    Say '---- 防火墙规则 ----' 'White'
    foreach ($rn in @($RuleName, $RdpRuleName)) {
        $r = Get-NetFirewallRule -DisplayName $rn -ErrorAction SilentlyContinue
        if ($r) { Say ("  [有] {0}  Enabled={1}" -f $rn, $r.Enabled) 'Green' }
        else     { Say ("  [无] {0}" -f $rn) 'DarkGray' }
    }

    Say '---- IP Helper 服务 ----' 'White'
    $svc = Get-Service iphlpsvc -ErrorAction SilentlyContinue
    if ($svc) { Say ("  iphlpsvc = {0}" -f $svc.Status) $(if ($svc.Status -eq 'Running') { 'Green' } else { 'Yellow' }) }

    Say '---- 远程桌面 ----' 'White'
    $deny = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server' -Name fDenyTSConnections -ErrorAction SilentlyContinue).fDenyTSConnections
    Say ("  fDenyTSConnections = {0}   ({1})" -f $deny, $(if ($deny -eq 0) { '已允许远程连接' } else { '远程连接被禁用' }))
}

if ($Status) { Show-Status; Say ''; Pause-Exit; exit 0 }

# ---------------- 撤销 ----------------
if ($Undo) {
    if (-not $isAdmin) { Say '撤销也需要管理员权限。' 'Red'; Pause-Exit; exit 1 }
    Say '正在撤销...' 'Yellow'
    (netsh interface portproxy delete v4tov4 listenaddress=0.0.0.0 listenport=$WebPort) 2>&1 | Out-Null
    (netsh interface portproxy delete v4tov4 listenaddress=$env:COMPUTERNAME listenport=$WebPort) 2>&1 | Out-Null
    Remove-NetFirewallRule -DisplayName $RuleName -ErrorAction SilentlyContinue
    Remove-NetFirewallRule -DisplayName $RdpRuleName -ErrorAction SilentlyContinue
    Say '  已删除 portproxy 规则与防火墙规则（远程桌面设置未改动）。' 'Green'
    Say ''
    Show-Status
    Pause-Exit
    exit 0
}

if (-not $isAdmin) {
    Say ''
    Say '  [X] 需要管理员权限。请关闭本窗口，右键 PowerShell -> 以管理员身份运行，再执行本脚本。' 'Red'
    Say ''
    Say '  如果实在拿不到管理员权限，可以手工执行下面两行（同样需要管理员）：' 'Yellow'
    Say ("    netsh interface portproxy add v4tov4 listenaddress=0.0.0.0 listenport={0} connectaddress=127.0.0.1 connectport={0}" -f $WebPort) 'White'
    Say ("    netsh advfirewall firewall add rule name=""{0}"" dir=in action=allow protocol=TCP localport={1}" -f $RuleName, $WebPort) 'White'
    Pause-Exit
    exit 1
}

# ---------------- 1. IP Helper ----------------
Say '---- 1/4 确保 IP Helper 服务运行 ----' 'White'
try {
    $svc = Get-Service iphlpsvc -ErrorAction Stop
    if ($svc.Status -ne 'Running') { Start-Service iphlpsvc; Say '  iphlpsvc 已启动' 'Green' }
    else { Say '  iphlpsvc 已在运行' 'Green' }
    Set-Service iphlpsvc -StartupType Automatic -ErrorAction SilentlyContinue
} catch { Say ("  [!] IP Helper 处理失败：{0}" -f $_.Exception.Message) 'Yellow' }

# ---------------- 2. portproxy ----------------
Say ''
Say '---- 2/4 添加 portproxy（外部 8888 -> 本机 127.0.0.1:8888）----' 'White'
# 先清掉可能存在的旧规则，避免重复
(netsh interface portproxy delete v4tov4 listenaddress=0.0.0.0 listenport=$WebPort) 2>&1 | Out-Null
$out = (netsh interface portproxy add v4tov4 listenaddress=0.0.0.0 listenport=$WebPort connectaddress=127.0.0.1 connectport=$WebPort) 2>&1 | Out-String
if ($out.Trim()) { Say ("  " + $out.Trim()) 'Yellow' } else { Say ("  [OK] 已添加 0.0.0.0:{0} -> 127.0.0.1:{0}" -f $WebPort) 'Green' }

# ---------------- 3. 防火墙 ----------------
Say ''
Say '---- 3/4 放行防火墙 ----' 'White'
if (-not (Get-NetFirewallRule -DisplayName $RuleName -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName $RuleName -Direction Inbound -Action Allow -Protocol TCP -LocalPort $WebPort -Profile Any -ErrorAction SilentlyContinue | Out-Null
}
if (Get-NetFirewallRule -DisplayName $RuleName -ErrorAction SilentlyContinue) { Say ("  [OK] TCP {0} 已放行" -f $WebPort) 'Green' }
else { Say '  [!] 防火墙规则创建失败，请手工放行' 'Yellow' }

# ICMP：让开发机能 ping 通，便于排障
if (-not (Get-NetFirewallRule -DisplayName 'MM-ICMP-In' -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName 'MM-ICMP-In' -Direction Inbound -Action Allow -Protocol ICMPv4 -IcmpType 8 -Profile Any -ErrorAction SilentlyContinue | Out-Null
}
if (Get-NetFirewallRule -DisplayName 'MM-ICMP-In' -ErrorAction SilentlyContinue) { Say '  [OK] 已允许 ICMPv4 ping（便于排障）' 'Green' }

# ---------------- 4. 可选远程桌面 ----------------
Say ''
Say '---- 4/4 远程桌面 ----' 'White'
if ($EnableRdp) {
    Set-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server' -Name fDenyTSConnections -Value 0 -ErrorAction SilentlyContinue
    # 规则组名随系统语言变化，中英文都试一遍
    (netsh advfirewall firewall set rule group="remote desktop" new enable=Yes) 2>&1 | Out-Null
    (netsh advfirewall firewall set rule group="远程桌面" new enable=Yes) 2>&1 | Out-Null
    New-NetFirewallRule -DisplayName $RdpRuleName -Direction Inbound -Action Allow -Protocol TCP -LocalPort 3389 -Profile Any -ErrorAction SilentlyContinue | Out-Null
    Say '  [OK] 已开通远程桌面 3389' 'Green'
    Say '  [!] 提醒：远程连接会占用现场控制台画面，生产运行中请谨慎。' 'Yellow'
} else {
    $deny = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server' -Name fDenyTSConnections -ErrorAction SilentlyContinue).fDenyTSConnections
    if ($deny -eq 0) { Say '  远程桌面本来就是开着的，未做改动。' 'DarkGray' }
    else { Say '  未开通远程桌面。需要时加 -EnableRdp 重跑。' 'DarkGray' }
}

# ---------------- 结果 ----------------
Say ''
Say ('=' * 64) 'Cyan'
Say '  配置完成，当前状态：' 'Cyan'
Say ('=' * 64) 'Cyan'
Show-Status
Say ''
Say '  下一步：在开发机浏览器打开 http://<本机IP>:8888/' 'Yellow'
Pause-Exit
