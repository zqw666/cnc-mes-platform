<#
    connect-ipc.ps1
    开发机 <--网线--> 现场工控机   一键配 IP + 全面探测

    用法（建议「以管理员身份」运行，配 IP 需要管理员权限）：
        .\connect-ipc.ps1                 # 自动选已插网线的有线网口，配好 IP 并全面探测
        .\connect-ipc.ps1 -Nic "以太网 4"  # 指定网口（名字或型号关键字）
        .\connect-ipc.ps1 -NoIp           # 只探测，不动网络配置
        .\connect-ipc.ps1 -Sweep          # 额外扫描 125.1-125.254 / 101.1-101.254 找存活主机
        .\connect-ipc.ps1 -Restore        # 撤销本脚本挂上的两个调试 IP，还原成原来样子

    说明：
      * 目标网段有两个：192.168.125.x（工控机侧）与 192.168.101.x（PLC / 机床 / 扫码枪侧）
      * 本脚本会同时给有线网口挂上两个 IP，两个网段一起通
      * 不给有线网口设网关（避免抢占 WLAN 的默认路由）
      * 结果同时写入 connect-report.txt，可直接复制成文字发回去
#>
[CmdletBinding()]
param(
    [string]$Nic = '',
    [switch]$NoIp,
    [switch]$Sweep,
    [switch]$Restore
)

$ErrorActionPreference = 'SilentlyContinue'

# ============ 目标清单 ============
$Targets = @(
    @{ ip = '192.168.125.201'; name = '工控机 MachineMonitor.exe'; key = $true },
    @{ ip = '192.168.101.10' ; name = 'PLC (OMRON FINS 9600/UDP)'; key = $true },
    @{ ip = '192.168.101.51' ; name = '机床3 (OPC UA)' },
    @{ ip = '192.168.101.52' ; name = '机床2 (OPC UA)' },
    @{ ip = '192.168.101.53' ; name = '机床1 (OPC UA)' },
    @{ ip = '192.168.101.71' ; name = '扫码枪 RCD' }
)
$Ports = @(
    @{ p = 8888; name = '后端控制页 HTTP' },
    @{ p = 3389; name = '远程桌面 RDP' },
    @{ p = 445 ; name = 'SMB 文件共享' },
    @{ p = 5985; name = 'WinRM / PSRemoting' },
    @{ p = 4026; name = 'VS 远程调试 msvsmon' },
    @{ p = 3306; name = 'MySQL' },
    @{ p = 1433; name = 'SQL Server' },
    @{ p = 4840; name = 'OPC UA' },
    @{ p = 5000; name = '扫码枪服务' }
)

$script:Report = New-Object System.Collections.ArrayList
$script:ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$script:LogFile = Join-Path $script:ScriptDir 'connect-report.txt'

# 本脚本会挂上的两个调试地址（-Restore 按这份清单撤销）
$DbgAddrs = @(
    @{ ip = '192.168.125.210'; desc = '工控机侧 125 网段' },
    @{ ip = '192.168.101.200'; desc = '设备侧 101 网段（PLC/机床）' }
)

function Say {
    param([string]$Msg, [string]$Color = 'Gray')
    Write-Host $Msg -ForegroundColor $Color
    [void]$script:Report.Add($Msg)
}
# 双击/手工运行时停一下看结果；被管道或脚本调用时不阻塞
function Pause-Exit {
    if (-not [Console]::IsInputRedirected) { [void](Read-Host '按回车键退出') }
}
# 中文按两个字符宽度算，保证控制台表格对齐
function Get-DispWidth {
    param([string]$s)
    $w = 0
    foreach ($ch in $s.ToCharArray()) {
        $c = [int][char]$ch
        if (($c -ge 0x1100 -and $c -le 0x115F) -or ($c -ge 0x2E80 -and $c -le 0xA4CF) -or
            ($c -ge 0xAC00 -and $c -le 0xD7A3) -or ($c -ge 0xF900 -and $c -le 0xFAFF) -or
            ($c -ge 0xFE30 -and $c -le 0xFE6F) -or ($c -ge 0xFF00 -and $c -le 0xFF60) -or
            ($c -ge 0xFFE0 -and $c -le 0xFFE6)) { $w += 2 } else { $w += 1 }
    }
    return $w
}
function Pad-Wide {
    param([string]$s, [int]$Width)
    $pad = $Width - (Get-DispWidth $s)
    if ($pad -lt 1) { $pad = 1 }
    return $s + (' ' * $pad)
}
function Head {
    param([string]$Msg)
    Say ''
    Say ('=' * 62) 'DarkCyan'
    Say "  $Msg" 'Cyan'
    Say ('=' * 62) 'DarkCyan'
}

function Test-Ping {
    param([string]$IP, [int]$TimeoutMs = 1000)
    $p = New-Object System.Net.NetworkInformation.Ping
    try {
        $r = $p.Send($IP, $TimeoutMs)
        return ($r.Status -eq 'Success')
    } catch { return $false }
    finally { $p.Dispose() }
}

function Test-Tcp {
    param([string]$IP, [int]$Port, [int]$TimeoutMs = 800)
    $c = New-Object System.Net.Sockets.TcpClient
    try {
        $iar = $c.BeginConnect($IP, $Port, $null, $null)
        $ok = $iar.AsyncWaitHandle.WaitOne($TimeoutMs, $false)
        if ($ok -and $c.Connected) { $c.EndConnect($iar); return $true }
        return $false
    } catch { return $false }
    finally { $c.Close() }
}

function Invoke-Sweep {
    param([string[]]$Segments)
    $list = New-Object System.Collections.ArrayList
    foreach ($seg in $Segments) {
        for ($i = 1; $i -le 254; $i++) { [void]$list.Add("$seg.$i") }
    }
    $jobs = New-Object System.Collections.ArrayList
    foreach ($ip in $list) {
        $p = New-Object System.Net.NetworkInformation.Ping
        [void]$jobs.Add([pscustomobject]@{ IP = $ip; Ping = $p; Task = $p.SendPingAsync($ip, 700) })
    }
    $tasks = New-Object 'System.Collections.Generic.List[System.Threading.Tasks.Task]'
    foreach ($j in $jobs) { [void]$tasks.Add($j.Task) }
    try { [void][System.Threading.Tasks.Task]::WaitAll($tasks.ToArray(), 5000) } catch { }

    $alive = New-Object System.Collections.ArrayList
    foreach ($j in $jobs) {
        try {
            if ($j.Task.IsCompleted -and $j.Task.Result.Status -eq 'Success') { [void]$alive.Add($j.IP) }
        } catch { }
        $j.Ping.Dispose()
    }
    return $alive
}

# ================= 0. 环境 =================
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

Head '环境检查'
Say ("本机名        : " + $env:COMPUTERNAME)
Say ("管理员权限    : " + $(if ($isAdmin) { '是' } else { '否（配 IP 会失败，请右键「以管理员身份运行」）' }))
Say ("时间          : " + (Get-Date).ToString('yyyy-MM-dd HH:mm:ss'))

# ================= 1. 网口 =================
Head '网口状态'
$wired = @(Get-NetAdapter -Physical | Where-Object { $_.MediaType -eq '802.3' })
if (-not $wired -or $wired.Count -eq 0) { $wired = @(Get-NetAdapter -Physical) }

Say '  ifIndex  网口名                         状态          速率'
Say '  -------  -----------------------------  ------------  ----------'
foreach ($a in $wired) {
    Say ("  {0,-7}  {1}  {2}  {3}" -f $a.ifIndex, (Pad-Wide $a.Name 29), (Pad-Wide $a.Status 12), $a.LinkSpeed)
}

# 选目标网口
if ($Nic) {
    $sel = $wired | Where-Object { $_.Name -eq $Nic -or $_.InterfaceDescription -like "*$Nic*" } | Select-Object -First 1
} else {
    $sel = $wired | Where-Object { $_.Status -eq 'Up' } | Select-Object -First 1
    if (-not $sel) { $sel = $wired | Select-Object -First 1 }
}

if (-not $sel) {
    Say ''
    Say '  [X] 没找到任何有线网口。请把网线插到本机的有线口后重跑本脚本。' 'Red'
} else {
    Say ''
    if ($sel.Status -ne 'Up') {
        Say ("  [!] 选中网口「{0}」当前是 {1} —— 网线没插好或对端没通电。" -f $sel.Name, $sel.Status) 'Yellow'
        Say '      请先插好网线，再重跑本脚本。' 'Yellow'
    } else {
        Say ("  [OK] 选中网口：{0}  (ifIndex={1}, 链路 {2})" -f $sel.Name, $sel.ifIndex, $sel.LinkSpeed) 'Green'
    }
}

# ================= 1.5 撤销（-Restore） =================
if ($Restore) {
    Head '撤销调试 IP'
    if (-not $sel) {
        Say '  [X] 没找到有线网口，无法撤销。' 'Red'
    } elseif (-not $isAdmin) {
        Say '  [X] 删除 IP 需要管理员权限，请以管理员身份重跑。' 'Red'
    } else {
        foreach ($w in $DbgAddrs) {
            $cur = Get-NetIPAddress -InterfaceIndex $sel.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue |
                   Where-Object { $_.IPAddress -eq $w.ip }
            if (-not $cur) { Say ("  [=] {0}/24  本来就不存在" -f $w.ip) 'DarkGray'; continue }
            $err = $null
            try { Remove-NetIPAddress -InterfaceIndex $sel.ifIndex -IPAddress $w.ip -PrefixLength 24 -Confirm:$false -ErrorAction Stop }
            catch { $err = $_.Exception.Message }
            if ($err) { Say ("  [X] {0}/24  删除失败：{1}" -f $w.ip, $err) 'Red' }
            else      { Say ("  [-] {0}/24  已移除  ({1})" -f $w.ip, $w.desc) 'Green' }
        }
        Say ''
        Say '  该网口剩余 IPv4：' 'White'
        $left = @(Get-NetIPAddress -InterfaceIndex $sel.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue)
        if ($left.Count -eq 0) { Say '    (无)' 'DarkGray' }
        else { foreach ($a in $left) { Say ("    {0}/{1}" -f $a.IPAddress, $a.PrefixLength) } }
        Say ''
        Say '  提示：若该网口原来是 DHCP，请到「网络连接」把它改回「自动获取 IP 地址」。' 'DarkGray'
    }
    Say ''
    Say ("报告已保存： " + $script:LogFile) 'Cyan'
    $script:Report | Out-File -FilePath $script:LogFile -Encoding UTF8
    Pause-Exit
    exit 0
}

# ================= 2. 配 IP =================
if (-not $NoIp -and $sel) {
    Head '配置本机 IP（双网段）'
    if (-not $isAdmin) {
        Say '  [X] 非管理员，跳过。请以管理员身份重跑，或手工在「网络连接」里添加下面两个 IP。' 'Red'
    } else {
        # 关闭 DHCP，避免自动获得奇怪地址
        try { Set-NetIPInterface -InterfaceIndex $sel.ifIndex -Dhcp Disabled -Confirm:$false } catch { }
        # 清掉 APIPA 169.254.x，免得干扰
        Get-NetIPAddress -InterfaceIndex $sel.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue |
            Where-Object { $_.IPAddress -like '169.254.*' } |
            ForEach-Object { try { Remove-NetIPAddress -InputObject $_ -Confirm:$false } catch { } }

        foreach ($w in $DbgAddrs) {
            $cur = Get-NetIPAddress -InterfaceIndex $sel.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue |
                   Where-Object { $_.IPAddress -eq $w.ip }
            if ($cur) {
                Say ("  [=] {0}/24  已存在  ({1})" -f $w.ip, $w.desc) 'DarkGray'
            } else {
                $err = $null
                try { New-NetIPAddress -InterfaceIndex $sel.ifIndex -IPAddress $w.ip -PrefixLength 24 -ErrorAction Stop | Out-Null }
                catch { $err = $_.Exception.Message }
                if ($err) { Say ("  [X] {0}/24  添加失败：{1}" -f $w.ip, $err) 'Red' }
                else      { Say ("  [+] {0}/24  已添加  ({1})" -f $w.ip, $w.desc) 'Green' }
            }
        }
        # 清 ARP 缓存，避免换口后认到旧 MAC
        try { arp -d * | Out-Null } catch { }
        Say '  提示：故意不设默认网关，WLAN 的上网路由不受影响。' 'DarkGray'
    }
}

# ================= 3. 当前 IP 汇总 =================
Head '本机当前 IPv4'
Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object { $_.IPAddress -ne '127.0.0.1' -and $_.IPAddress -notlike '169.254.*' } |
    Sort-Object InterfaceAlias |
    ForEach-Object { Say ("  {0} {1} /{2}" -f (Pad-Wide $_.InterfaceAlias 24), (Pad-Wide $_.IPAddress 18), $_.PrefixLength) }

# ================= 4. 连通性 =================
Head '设备连通性（Ping）'
$aliveMap = @{}
foreach ($t in $Targets) {
    $ok = Test-Ping $t.ip
    $aliveMap[$t.ip] = $ok
    $tag = if ($ok) { '[OK]' } else { '[--]' }
    $col = if ($ok) { 'Green' } else { 'DarkGray' }
    Say ("  {0}  {1,-18}  {2}" -f $tag, $t.ip, $t.name) $col
}

# ================= 5. 端口探测 =================
Head '关键端口探测（仅对 Ping 通的主机）'
$portMap = @{}
foreach ($t in $Targets) {
    if (-not $aliveMap[$t.ip]) { continue }
    Say ''
    Say ("  ---- {0}  ({1}) ----" -f $t.ip, $t.name) 'White'
    foreach ($pt in $Ports) {
        $open = Test-Tcp -IP $t.ip -Port $pt.p
        $portMap["$($t.ip):$($pt.p)"] = $open
        $tag = if ($open) { 'OPEN  ' } else { 'closed' }
        $col = if ($open) { 'Green' } else { 'DarkGray' }
        Say ("    {0}  {1,-6} {2}" -f $tag, $pt.p, $pt.name) $col
    }
}

# ================= 6. 可选全网段扫描 =================
if ($Sweep) {
    Head '网段扫描（找存活主机）'
    Say '  正在扫描 192.168.125.1-254 与 192.168.101.1-254 ...' 'DarkGray'
    $alive = Invoke-Sweep -Segments @('192.168.125', '192.168.101')
    if ($alive.Count -eq 0) {
        Say '  没有发现任何存活主机。' 'Yellow'
    } else {
        foreach ($ip in ($alive | Sort-Object { [int]($_ -split '\.')[-1] })) {
            $mac = ''
            try {
                $n = Get-NetNeighbor -IPAddress $ip -ErrorAction SilentlyContinue | Select-Object -First 1
                if ($n) { $mac = $n.LinkLayerAddress }
            } catch { }
            Say ("  [OK] {0,-18} MAC={1}" -f $ip, $mac) 'Green'
        }
    }
}

# ================= 7. 结论 =================
Head '结论与下一步'
$ipcPing = $aliveMap['192.168.125.201']
$plcPing = $aliveMap['192.168.101.10']
$webOpen = $portMap['192.168.125.201:8888']
$rdpOpen = $portMap['192.168.125.201:3389']
$smbOpen = $portMap['192.168.125.201:445']

if ($ipcPing) {
    Say '  [OK] 工控机可达，物理链路已经通了。' 'Green'
    if ($webOpen) {
        Say '  [OK] 8888 端口已开放 —— 直接用浏览器打开：' 'Green'
        Say '         http://192.168.125.201:8888/' 'White'
    } else {
        Say '  [!] 工控机通，但 8888 没开。因为后端只监听 localhost（Program.cs:24-27）。' 'Yellow'
        Say '      解决：在工控机上以管理员运行一次 ipc-setup.ps1（做 portproxy + 放行防火墙），' 'Yellow'
        Say '      之后本机浏览器就能直接开 http://192.168.125.201:8888/' 'Yellow'
    }
    if ($rdpOpen) { Say '  [OK] 3389 远程桌面已开 —— 可直接 mstsc 连上操作工控机桌面。' 'Green' }
    else          { Say '  [--] 3389 未开。ipc-setup.ps1 -EnableRdp 可开通（注意：会抢占现场控制台画面）。' 'DarkGray' }
    if ($smbOpen) { Say '  [OK] 445 已开 —— 可直接用 \\192.168.125.201\ 读写文件，不再受「只能传文字」限制。' 'Green' }
    else          { Say '  [--] 445 未开。用 serve-files.ps1 在本机起个下载服务，在工控机浏览器里拉文件即可。' 'DarkGray' }
} elseif ($plcPing) {
    Say '  [!] PLC 通了，但工控机 125.201 不通。' 'Yellow'
    Say '      说明网线落在「设备段 101」的交换机上，工控机在这一侧可能是另一个 IP。' 'Yellow'
    Say '      建议：重跑并加 -Sweep 找出另一台存活主机，或到工控机上 ipconfig 看它 101 侧地址。' 'Yellow'
} else {
    Say '  [X] 两个网段都不通。逐项排查：' 'Red'
    Say '      1) 网线插的是本机有线口，不是 WLAN；接口状态要显示 Up' 'Red'
    Say '      2) 对端交换机是否通电、网口灯是否亮' 'Red'
    Say '      3) 工控机 / 交换机是否在别的网段 —— 用 -Sweep 扫一遍' 'Red'
    Say '      4) 本机防火墙是否拦了 ICMP（工控机默认可能不响应 ping，但端口探测仍有效）' 'Red'
}

# ================= 8. 落盘 =================
Say ''
Say ('=' * 62) 'DarkCyan'
Say ("报告已保存： " + $script:LogFile) 'Cyan'
Say '（纯文本，可直接复制粘贴到内部平台发回来）' 'Cyan'
$script:Report | Out-File -FilePath $script:LogFile -Encoding UTF8

Write-Host ''
Write-Host '按回车键退出...' -ForegroundColor DarkGray
Pause-Exit
