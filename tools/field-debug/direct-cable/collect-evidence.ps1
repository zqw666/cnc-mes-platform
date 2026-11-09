<#
    collect-evidence.ps1
    现场取证 —— 把"程序到底走到哪一步、为什么停"的证据一次性取回来

    为什么需要它：
      这个项目的 LogHelper 默认 FileEnabled=false / DatabaseEnabled=true（日志只写库不落文件），
      而代码里存在多处「空 catch」，异常会被静默吞掉。于是故障常常表现为「流程跑一半就结束、
      什么提示都没有」。本脚本把三层证据（配置 / 数据库 / 进程与环境）一次性抓出来，
      用"最后成功的步骤"反推卡点，不依赖断点。

    运行位置：
      在工控机上运行（推荐，证据最全）：
          .\collect-evidence.ps1 -OnIpc
      在开发机上运行（网络通了就能用，采端口/控制页/数据库三块）：
          .\collect-evidence.ps1 -IpcIp 192.168.125.201
      数据库账号不同时：
          .\collect-evidence.ps1 -OnIpc -SqlUser sa -SqlPass 你的密码

    产出：evidence-<时间戳>.md —— Markdown 纯文本，可直接走内部平台回传。
    本脚本只读，不修改任何配置、不写任何数据库。
#>
[CmdletBinding()]
param(
    [string]$IpcIp = '192.168.125.201',
    [string]$SqlHost = '',
    [int]$SqlPort = 1433,
    [string]$SqlDb = 'qianduantest',
    [string]$SqlUser = 'GREE',
    [string]$SqlPass = 'qwe!23',
    [switch]$OnIpc,
    [string]$DeployRoot = '',
    [string]$OutFile = ''
)

$ErrorActionPreference = 'SilentlyContinue'

if (-not $SqlHost) { $SqlHost = $IpcIp }
if (-not $OutFile) {
    $stamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
    $OutFile = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Definition) "evidence-$stamp.md"
}

$script:L = New-Object System.Collections.ArrayList
function Say { param([string]$M = '') Write-Host $M; [void]$script:L.Add($M) }
function H1 { param([string]$M) Say ''; Say "# $M"; Say '' }
function H2 { param([string]$M) Say ''; Say "## $M"; Say '' }
function H3 { param([string]$M) Say ''; Say "### $M"; Say '' }
function Note { param([string]$M) Say "> $M" }
function Code { param([string]$M) Say '```'; Say $M; Say '```' }
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

function Test-Tcp {
    param([string]$IP, [int]$Port, [int]$TimeoutMs = 900)
    $c = New-Object System.Net.Sockets.TcpClient
    try {
        $iar = $c.BeginConnect($IP, $Port, $null, $null)
        if ($iar.AsyncWaitHandle.WaitOne($TimeoutMs, $false) -and $c.Connected) { $c.EndConnect($iar); return $true }
        return $false
    } catch { return $false }
    finally { $c.Close() }
}

function Show-Dt {
    param($Res, [string[]]$Cols, [int]$Max = 20)
    if ($Res -and $Res.Err) { Say ("_查询出错：{0}_" -f $Res.Err); Say ''; return }
    if ($null -eq $Res -or $null -eq $Res.Rows -or $Res.Rows.Count -eq 0) { Say '_（无记录）_'; Say ''; return }
    $cols = if ($Cols) { @($Cols | Where-Object { $Res.Cols -contains $_ }) } else { @($Res.Cols) }
    if ($cols.Count -eq 0) { Say '_（无可见列）_'; Say ''; return }
    $idx = @{}
    for ($i = 0; $i -lt $Res.Cols.Count; $i++) { $idx[$Res.Cols[$i]] = $i }
    Say ('| ' + ($cols -join ' | ') + ' |')
    Say ('|' + (($cols | ForEach-Object { ' --- ' }) -join '|') + '|')
    $n = 0
    foreach ($row in $Res.Rows) {
        if ($n -ge $Max) { break }
        $vals = @()
        foreach ($c in $cols) {
            $v = '' + $row[$idx[$c]]
            $v = $v.Replace("`r", ' ').Replace("`n", ' ').Replace('|', '/')
            if ($v.Length -gt 160) { $v = $v.Substring(0, 160) + '…' }
            if ($v -eq '') { $v = ' ' }
            $vals += $v
        }
        Say ('| ' + ($vals -join ' | ') + ' |')
        $n++
    }
    if ($Res.Rows.Count -gt $Max) { Say ''; Say ("_（共 {0} 行，只显示前 {1} 行）_" -f $Res.Rows.Count, $Max) }
    Say ''
}

# 用 DataReader 手工取值，不依赖 SqlDataAdapter（后者在 PS 里构造易静默失败）
function Invoke-Sql {
    param($Conn, [string]$Sql, [int]$Timeout = 20)
    $res = [pscustomobject]@{ Cols = @(); Rows = (New-Object System.Collections.ArrayList); Err = '' }
    $cmd = $Conn.CreateCommand()
    $cmd.CommandText = $Sql
    $cmd.CommandTimeout = $Timeout
    $rd = $null
    try {
        $rd = $cmd.ExecuteReader()
    } catch {
        $res.Err = $_.Exception.Message
        return $res
    }
    try {
        $cols = @()
        for ($i = 0; $i -lt $rd.FieldCount; $i++) { $cols += $rd.GetName($i) }
        $res.Cols = $cols
        while ($rd.Read()) {
            $vals = @()
            for ($i = 0; $i -lt $rd.FieldCount; $i++) {
                if ($rd.IsDBNull($i)) { $vals += '' } else { $vals += ('' + $rd.GetValue($i)) }
            }
            [void]$res.Rows.Add($vals)
        }
    } catch {
        $res.Err = $_.Exception.Message
    } finally {
        if ($rd) { $rd.Close() }
    }
    return $res
}

function Table-Exists {
    param($Conn, [string]$Name)
    $r = Invoke-Sql $Conn ("select count(*) as n from sys.tables where name = '" + $Name.Replace("'", "''") + "'")
    if ($r.Err -or $r.Rows.Count -eq 0) { return $false }
    return ([int]$r.Rows[0][0] -gt 0)
}

# 依次尝试多条 SQL，返回第一条不报错的结果。
# 用途：现场库的列名可能与开发机副本不同，不假设 schema，先试带排序的，失败就退化成 select *。
function Invoke-SqlTry {
    param($Conn, [string[]]$Sqls, [int]$Timeout = 20)
    $last = $null
    foreach ($q in $Sqls) {
        $r = Invoke-Sql $Conn $q $Timeout
        if (-not $r.Err) { return $r }
        $last = $r
    }
    return $last
}

# ============================================================
H1 '现场取证报告'
$now = Get-Date
Code ("采集时间   : " + $now.ToString('yyyy-MM-dd HH:mm:ss') + `
      "`n采集主机   : " + $env:COMPUTERNAME + " (" + $env:USERNAME + ")" + `
      "`n运行模式   : " + $(if ($OnIpc) { '工控机本机（全量）' } else { '远程（端口/控制页/数据库）' }) + `
      "`n目标工控机 : " + $IpcIp + `
      "`n数据库     : " + $SqlHost + ':' + $SqlPort + ' / ' + $SqlDb + ' / ' + $SqlUser)

# ============================================================
H2 '一、入口与端口'
$portDefs = @(
    @{ p = 8888; n = '后端控制页 HTTP' },
    @{ p = 3389; n = '远程桌面 RDP' },
    @{ p = 445 ; n = 'SMB 文件共享' },
    @{ p = 5985; n = 'WinRM' },
    @{ p = 4026; n = 'VS 远程调试' },
    @{ p = $SqlPort; n = 'SQL Server' }
)
$portState = @{}
foreach ($d in $portDefs) {
    $ok = Test-Tcp -IP $IpcIp -Port $d.p
    $portState[$d.p] = $ok
    Say ("- {0}  {1,-6}  {2}" -f $(if ($ok) { '**[开]**' } else { '关闭　' }), $d.p, $d.n)
}
Say ''

if ($portState[8888]) {
    try {
        $r = Invoke-WebRequest -Uri ("http://" + $IpcIp + ":8888/") -UseBasicParsing -TimeoutSec 8
        Say ("控制页响应：HTTP {0}，{1} 字节" -f $r.StatusCode, $r.RawContentLength)
    } catch {
        Say ("控制页请求失败：{0}" -f $_.Exception.Message)
    }
} elseif ($portState[3389]) {
    Note '8888 未开，但 3389 通 —— 可以先 RDP 进工控机，在本地浏览器开 `http://localhost:8888/`'
} else {
    Note '8888 未开：后端只监听 localhost（Program.cs:24-27）。在工控机跑一次 ipc-setup.ps1 做 portproxy 即可'
}

# ============================================================
H2 '二、部署真实性核对'
$root = $DeployRoot
if (-not $root -and $OnIpc) {
    foreach ($cand in @("$PSScriptRoot\..\..\..\web-ui\backend\bin\Any CPU\Debug\net472", "$PSScriptRoot\..\backend\bin\Any CPU\Debug\net472", 'D:\MachineMonitor' , 'D:\programauto', 'C:\MachineMonitor')) {
        if ($cand -and (Test-Path -LiteralPath $cand)) { $root = (Resolve-Path -LiteralPath $cand).Path; break }
    }
    if (-not $root) { $root = (Get-Location).Path }
}
if (-not $root) {
    Note '未指定 -DeployRoot 且非本机模式，跳过。在工控机上跑时用 -DeployRoot "部署目录" 指定。'
} elseif (-not (Test-Path -LiteralPath $root)) {
    Say ("目录不存在：{0}" -f $root)
} else {
    Say ("部署目录：``{0}``" -f $root)
    Say ''
    $files = @()
    foreach ($p in @('MachineMonitor.exe', 'config.json', 'index.html', 'app.js')) {
        $f = Join-Path $root $p
        if (Test-Path -LiteralPath $f) { $files += Get-Item -LiteralPath $f }
    }
    $subsDir = Join-Path $root 'subs'
    if (Test-Path -LiteralPath $subsDir) {
        $files += Get-ChildItem -LiteralPath $subsDir -Filter *.exe | Sort-Object Name
    }
    Say '| 文件 | 修改时间 | 大小 | 备注 |'
    Say '| --- | --- | --- | --- |'
    $exeTime = $null
    foreach ($f in $files) {
        $rel = $f.FullName
        if ($rel.StartsWith($root)) { $rel = $rel.Substring($root.Length).TrimStart('\') }
        $row = "| ``{0}`` | {1} | {2:N0} B |  |" -f $rel, $f.LastWriteTime.ToString('MM-dd HH:mm'), $f.Length
        if ($f.Name -eq 'MachineMonitor.exe') { $exeTime = $f.LastWriteTime }
        Say $row
    }
    Say ''

    # 主程序与子程序时间不一致 → 部署不完整
    if ($exeTime) {
        $subs = @($files | Where-Object { $_.Name -ne 'MachineMonitor.exe' })
        $stale = @($subs | Where-Object { [Math]::Abs(($exeTime - $_.LastWriteTime).TotalHours) -gt 24 })
        if ($stale.Count -gt 0) {
            Say ("**注意：有 {0} 个文件与主程序 exe 的时间相差超过 24 小时 —— 可能是只换了一部分，部署不完整。**" -f $stale.Count)
            foreach ($s in $stale) { Say ("  - ``{0}``  {1}" -f $s.Name, $s.LastWriteTime.ToString('MM-dd HH:mm')) }
            Say ''
        } else {
            Say '主程序与子程序修改时间接近，不像只换了一半。'
            Say ''
        }
    }

    # config.json 关键项
    $cfgPath = Join-Path $root 'config.json'
    if (Test-Path -LiteralPath $cfgPath) {
        H3 'config.json 关键项'
        try {
            $raw = [IO.File]::ReadAllText($cfgPath, [Text.Encoding]::UTF8)
            $cfg = $raw | ConvertFrom-Json
            $say2 = {
                param($k, $v)
                Say ("- ``{0}`` = ``{1}``" -f $k, $v)
            }
            & $say2 'database.type' $cfg.database.type
            & $say2 'eman.host' ($cfg.eman.host)
            & $say2 'serverIp' $cfg.serverIp
            & $say2 'line.gripperType' $cfg.line.gripperType
            & $say2 'line.palletEnabled' $cfg.line.palletEnabled
            & $say2 'plc.ip' ($cfg.plc.ip)
            if ($cfg.racks) {
                Say ("- ``racks`` 共 {0} 个：" -f @($cfg.racks).Count)
                foreach ($rk in $cfg.racks) {
                    Say ("  - {0} / type={1} / {2}×{3} / grab={4} place={5} scan={6}" -f `
                        $rk.name, $rk.type, $rk.rows, $rk.cols, $rk.grabStartTaskNo, $rk.placeStartTaskNo, $rk.scanStartTaskNo)
                }
            }
            if ($cfg.grippers) {
                foreach ($g in $cfg.grippers) {
                    Say ("- ``grippers`` {0} / type={1} / grab={2} place={3}" -f $g.name, $g.type, $g.grabTaskNo, $g.placeTaskNo)
                }
            }
            Say ''
            # 已知半成品改造：nc_server 无人读、share_server 不存在
            $hasNc = $null -ne $cfg.nc_server
            $hasShare = $null -ne $cfg.share_server
            Say ("- ``nc_server`` 节点：{0}　|　``share_server`` 节点：{1}" -f `
                $(if ($hasNc) { '存在' } else { '不存在' }), $(if ($hasShare) { '存在' } else { '不存在' }))
            if ($hasNc -and -not $hasShare) {
                Note '已知坑：`nc_server` 配置存在但**无任何代码读取**，而 `load_machine` / `load_aluminum` / `change_machine` 的 transfer.cs 读的是 `share_server`（该节点不存在）→ 这三个程序实际在用各自硬编码的 `\\192.168.1.100\share`。NC 路径类故障先查这里。'
            }
            if ($hasNc) {
                Say ''
                Say ("  - ``nc_server.root_path`` = ``{0}``" -f $cfg.nc_server.root_path)
                Say ("  - ``nc_server.local_temp`` = ``{0}``" -f $cfg.nc_server.local_temp)
            }
            Say ''
        } catch {
            Say ("config.json 解析失败：{0}" -f $_.Exception.Message)
            Say ''
        }
    }
}

# ============================================================
H2 '三、数据库取证（核心）'
$conn = $null
$dbOk = $false
try {
    $cs = "Server={0},{1};Database={2};User Id={3};Password={4};TrustServerCertificate=True;Connect Timeout=6;" -f $SqlHost, $SqlPort, $SqlDb, $SqlUser, $SqlPass
    $conn = New-Object System.Data.SqlClient.SqlConnection $cs
    $conn.Open()
    $dbOk = $true
    Say ("连接成功，SQL Server 版本 {0}" -f $conn.ServerVersion)
    Say ''
} catch {
    Say ("**数据库连接失败：{0}**" -f $_.Exception.Message)
    Say ''
    Note '若 1433 端口关闭，说明 SQL Server 未允许远程连接（SQL Server 配置管理器 → TCP/IP → 启用 + 重启服务），或在工控机上用 -OnIpc 本机采集。'
}

if ($dbOk) {
    H3 '3.1 表是否存在'
    $wantTables = @('work_flow', 'process_flow', 'workflow_step', 'workflow_log', 'process_status', 'process_log',
                    'macmessage', 'rack_administrate', 'robotmessage', 'grippers', 'following_rack', 'macworkposstatus')
    $exist = @{}
    $sayt = '| 表 | 是否存在 | 行数 | 作用 |'
    $roleMap = @{
        'work_flow'          = '当前启用的步骤与优先级（调度器读它）'
        'process_flow'       = 'work_flow 的回退表'
        'workflow_step'      = '步骤级执行状态（running/failed）'
        'workflow_log'       = '启动与步骤执行记录'
        'process_status'     = '流程状态'
        'process_log'        = '业务日志主表（LogHelper 写这里）'
        'macmessage'         = '机床表'
        'rack_administrate'  = '料位表'
        'robotmessage'       = '机器人/夹爪状态'
        'grippers'           = '夹爪定义（代码未读取）'
        'following_rack'     = '随行台'
        'macworkposstatus'   = '机床工位状态'
    }
    Say $sayt
    Say '| --- | --- | --- | --- |'
    foreach ($t in $wantTables) {
        if (Table-Exists $conn $t) {
            $dt = Invoke-Sql $conn ("select count(*) as n from [" + $t + "]")
            $n = [int]$dt.Rows[0][0]
            $exist[$t] = $n
            Say ("| ``{0}`` | 有 | {1} | {2} |" -f $t, $n, $roleMap[$t])
        } else {
            $exist[$t] = -1
            Say ("| ``{0}`` | **不存在** | - | {1} |" -f $t, $roleMap[$t])
        }
    }
    Say ''
    if ($exist['process_log'] -eq -1) {
        Note '`process_log` 表不存在 → **本库没有任何业务日志可查**。LogHelper 默认 DatabaseEnabled=true 且 FileEnabled=false，日志上报会静默落空。这是"程序出错但看不到任何提示"的直接原因之一。'
        Note '修法：让 LogHelper 同时开文件日志（FileEnabled=true），或先用 `DatabaseRepository` 补建 process_log 表。'
    }

    H3 '3.2 当前启用的步骤顺序（work_flow）'
    if ($exist['work_flow'] -gt 0) {
        $dt = Invoke-SqlTry $conn @('select * from work_flow order by priority desc, id asc', 'select * from work_flow')
        Show-Dt $dt $null 30
        Note '列名以实际库为准（本脚本不做假设）。重点看：启用/禁用标志、顺序字段、步骤名与实际 `subs/*.exe` 文件名是否对得上 —— 项目记忆里出现过 `preparematerial` 这类别名，别名与 exe 名不一致会导致调度匹配不上。'
        Say ''
    } else {
        Say '_work_flow 为空。_' ; Say ''
    }

    H3 '3.3 步骤级状态（workflow_step）—— 定位卡点最关键的表'
    if ($exist['workflow_step'] -gt 0) {
        $dt = Invoke-SqlTry $conn @('select top 30 * from workflow_step order by id desc', 'select top 30 * from workflow_step')
        Show-Dt $dt $null 30
    } else {
        Say '_无记录。_'
        Note '**这说明调度器从未成功登记过任何步骤**。结合 work_flow 有内容 → 要么主程序没真正跑到调度，要么一进步骤就异常退出（异常被空 catch 吞掉，所以什么都没留下）。'
        Say ''
    }

    H3 '3.4 运行轨迹（workflow_log，最近 20 条）'
    if ($exist['workflow_log'] -gt 0) {
        $dt = Invoke-SqlTry $conn @('select top 20 * from workflow_log order by id desc', 'select top 20 * from workflow_log')
        Show-Dt $dt $null 20
        $dt2 = Invoke-Sql $conn 'select min(id) as firstid, max(id) as lastid, count(*) as n from workflow_log'
        if ($dt2.Rows.Count -gt 0) {
            Say ("共 {0} 条。注意看时间戳：若全部集中在某一天且都是启动日志，说明**系统从未真正跑过工作流**。" -f $dt2.Rows[0][0])
            Say ''
        }
    } else {
        Say '_无记录。_' ; Say ''
    }

    H3 '3.5 业务日志（process_log，最近 50 条）'
    if ($exist['process_log'] -gt 0) {
        $dt = Invoke-SqlTry $conn @('select top 50 * from process_log order by id desc', 'select top 50 * from process_log')
        Show-Dt $dt $null 50
    } else {
        Say '_表不存在，无法取证。_' ; Say ''
    }

    H3 '3.6 关键业务表现状'
    foreach ($t in @('robotmessage', 'macmessage', 'rack_administrate', 'macworkposstatus', 'following_rack')) {
        if ($exist[$t] -gt 0) {
            $dt = Invoke-Sql $conn ("select top 10 * from [" + $t + "]")
            Say ("**``{0}``（{1} 行）**" -f $t, $exist[$t])
            Say ''
            Show-Dt $dt $null 10
        } elseif ($exist[$t] -eq 0) {
            Say ("**``{0}``：0 行（空表）**" -f $t)
            Say ''
        }
    }

    $conn.Close()
}

# ============================================================
if ($OnIpc) {
    H2 '四、进程与环境（工控机本机）'
    H3 '4.1 主程序进程'
    $procs = @(Get-Process -Name 'MachineMonitor' -ErrorAction SilentlyContinue)
    if ($procs.Count -eq 0) {
        Say '**MachineMonitor.exe 未在运行。**'
        Note '若此时 8888 却是通的，说明是残留进程；若 8888 不通，那这个现象本身就解释了"页面打不开"。'
    } else {
        Say '| PID | 启动时间 | CPU(s) | 内存(MB) | 路径 |'
        Say '| --- | --- | --- | --- | --- |'
        foreach ($p in $procs) {
            $cpu = 0; try { $cpu = [Math]::Round($p.TotalProcessorTime.TotalSeconds, 1) } catch { }
            Say ("| {0} | {1} | {2} | {3} | ``{4}`` |" -f $p.Id, $p.StartTime.ToString('MM-dd HH:mm:ss'), $cpu, [Math]::Round($p.WorkingSet64 / 1MB, 1), $p.Path)
        }
    }
    Say ''
    Say '子程序进程：'
    $subProcs = @(Get-Process | Where-Object { $_.Path -like '*\subs\*' })
    if ($subProcs.Count -eq 0) { Say '- （无，工作流当前不在执行中）' }
    else { foreach ($p in $subProcs) { Say ("- {0}  PID={1}  启动={2}" -f $p.ProcessName, $p.Id, $p.StartTime.ToString('HH:mm:ss')) } }
    Say ''

    H3 '4.2 停止标志与监听'
    $stopFlag = $false
    if (-not $DeployRoot) { $DeployRoot = (Get-Location).Path }
    foreach ($cand in @((Join-Path $DeployRoot 'subs\stop.flag'), (Join-Path $DeployRoot 'stop.flag'))) {
        if (Test-Path -LiteralPath $cand) {
            $stopFlag = $true
            Say ("- **存在停止标志文件**：``{0}``（修改于 {1}）" -f $cand, (Get-Item -LiteralPath $cand).LastWriteTime)
        }
    }
    if (-not $stopFlag) { Say '- 无 stop.flag（工作流未处于"要求停止"状态）' }
    $ns = @(netstat -ano | Select-String ':8888\s')
    if ($ns.Count -eq 0) { Say '- 8888 无监听进程' } else { foreach ($l in $ns) { Say ("- " + $l.ToString().Trim()) } }
    $pp = (netsh interface portproxy show v4tov4) 2>&1 | Out-String
    if ($pp.Trim()) { Say '- portproxy 规则：' ; Code $pp.Trim() } else { Say '- 无 portproxy 规则' }
    Say ''

    H3 '4.3 系统事件日志（近 7 天，错误级，最近 20 条）'
    try {
        $evts = @(Get-WinEvent -FilterHashtable @{ LogName = 'Application'; Level = 1, 2; StartTime = (Get-Date).AddDays(-7) } -MaxEvents 20 -ErrorAction Stop)
        if ($evts.Count -eq 0) { Say '_无错误事件。_' }
        else {
            Say '| 时间 | 来源 | ID | 摘要 |'
            Say '| --- | --- | --- | --- |'
            foreach ($e in $evts) {
                $m = '' + $e.Message
                $m = $m.Replace("`r", ' ').Replace("`n", ' ')
                if ($m.Length -gt 140) { $m = $m.Substring(0, 140) + '…' }
                Say ("| {0} | {1} | {2} | {3} |" -f $e.TimeCreated.ToString('MM-dd HH:mm:ss'), $e.ProviderName, $e.Id, $m)
            }
            Say ''
            $netErr = @($evts | Where-Object { $_.ProviderName -like '*NET Runtime*' -or $_.ProviderName -like '*.NET*' -or $_.Message -like '*未处理*' })
            if ($netErr.Count -gt 0) {
                Note '**发现 .NET 运行时未处理异常记录 —— 这是绕过所有空 catch 的硬证据，优先看这几条。**'
            }
        }
    } catch {
        Say ("事件日志读取失败：{0}" -f $_.Exception.Message)
    }
    Say ''
}

# ============================================================
$tipNo = if ($OnIpc) { '五' } else { '四' }
H2 ($tipNo + '、怎么读这份报告（定位卡点的三步法）')
Say '1. **先看 `workflow_step` 最后一条** —— 有记录就看它的 `status`（running 卡住 / failed 失败）；'
Say '   如果这张表是空的，说明异常在登记步骤之前就发生了。'
Say '2. **再看 `process_log` / `workflow_log` 最后一条的时间与内容** —— 它标出"程序最后承认做过的事"。'
Say '   这条之后的第一个业务动作，就是嫌疑点。'
Say '3. **最后用现场信号交叉验证**（这一步不依赖任何日志）：'
Say '   - 读 PLC `robot.taskNumber`（D100）：程序到底有没有下发任务号？值对不对？'
Say '   - 读 `robot.taskStart`（104.00）：有没有发出过启动脉冲？'
Say '   - 读 `robotmessage.clampposstatus`：夹爪状态有没有被写进去？'
Say '   日志被吞掉时，**PLC 侧的动作序列是唯一客观证据**。'
Say ''
Note '如果三层全是空的（表空 + 日志无 + 事件无 + PLC 无动作），那基本可以断定：**异常发生在最早的配置加载阶段，且被空 catch 吞掉了** → 这时必须开文件日志或上断点，靠库是查不出来的。'

Say ''
Say '---'
Say ("报告生成完毕：``{0}``" -f $OutFile)
Say ''
Say '（Markdown 纯文本，可直接复制粘贴到内部平台回传）'

$script:L | Out-File -FilePath $OutFile -Encoding UTF8
Write-Host ''
Write-Host ("已写入: " + $OutFile) -ForegroundColor Green
Pause-Exit
