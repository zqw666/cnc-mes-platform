# ============================================================
# 现场综合诊断（在工控机 / 现场机器上运行）
# 输出一份精简报告，可整段复制，通过内部平台回传
# 用法：.\field-diagnose.ps1
#       .\field-diagnose.ps1 -AppDir "D:\path\to\app"
# ============================================================
param(
    [string]$AppDir = '',
    [string]$DbServer = '127.0.0.1,1433',
    [string]$DbUser = 'GREE',
    [string]$DbPass = 'qwe!23',
    [string]$DbName = 'qianduantest'
)

try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }

$out = New-Object System.Collections.ArrayList
function Emit($s) {
    [void]$out.Add([string]$s)
    Write-Host $s
}

Emit ("=" * 58)
Emit ("现场诊断报告   " + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
Emit ("=" * 58)

# ---------- 1. 主机 ----------
Emit ""
Emit "--- 1. 主机 ---"
Emit ("  主机名 : " + $env:COMPUTERNAME)
Emit ("  用户   : " + $env:USERNAME)
$os = Get-CimInstance Win32_OperatingSystem -ErrorAction SilentlyContinue
if ($os) { Emit ("  系统   : " + $os.Caption + "  Build " + $os.BuildNumber) }
Emit ("  架构   : " + $env:PROCESSOR_ARCHITECTURE)

# ---------- 2. 网络 ----------
Emit ""
Emit "--- 2. 网络 ---"
Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object { $_.IPAddress -ne '127.0.0.1' } |
    ForEach-Object { Emit ("  " + $_.InterfaceAlias + " : " + $_.IPAddress + "/" + $_.PrefixLength) }

# ---------- 3. 后端进程与端口 ----------
Emit ""
Emit "--- 3. 后端进程 / 端口 ---"
$proc = Get-Process MachineMonitor -ErrorAction SilentlyContinue
if ($proc) {
    foreach ($p in $proc) {
        Emit ("  MachineMonitor 运行中  PID=" + $p.Id + "  启动于 " + $p.StartTime.ToString('MM-dd HH:mm'))
    }
} else {
    Emit "  MachineMonitor 未运行"
}
$listen = netstat -ano | Select-String ':8888'
if ($listen) {
    foreach ($l in $listen) { Emit ("  " + $l.ToString().Trim()) }
} else {
    Emit "  8888 端口未监听"
}

# ---------- 4. 应用目录与关键配置 ----------
Emit ""
Emit "--- 4. 应用目录 / 关键配置 ---"
if (-not $AppDir) {
    $cands = @((Get-Location).Path, (Split-Path (Get-Location).Path -Parent), 'D:\MachineMonitor', 'C:\MachineMonitor')
    foreach ($c in $cands) {
        if ($c -and (Test-Path (Join-Path $c 'config.json'))) { $AppDir = $c; break }
    }
}

if ($AppDir -and (Test-Path (Join-Path $AppDir 'config.json'))) {
    Emit ("  配置目录 : " + $AppDir)
    try {
        $cfg = Get-Content (Join-Path $AppDir 'config.json') -Raw | ConvertFrom-Json
        Emit ("  database : " + $cfg.database.type + "  " + $cfg.database.host + ":" + $cfg.database.port + "/" + $cfg.database.database)
        Emit ("  serverIp : " + $cfg.serverIp)
        Emit ("  plc.ip   : " + $cfg.plc.ip + ":" + $cfg.plc.port)
        $mip = ($cfg.machines.PSObject.Properties | ForEach-Object { $_.Value.ip }) -join ', '
        Emit ("  machines : " + $mip)
        $rk = ($cfg.racks | ForEach-Object { $_.id + "/" + $_.type + "(" + $_.rows + "x" + $_.cols + ")" }) -join ', '
        Emit ("  racks    : " + $rk)
        $gr = ($cfg.grippers | ForEach-Object { $_.type + "/" + $_.name }) -join ', '
        Emit ("  grippers : " + $gr)
        Emit ("  line     : gripperType=" + $cfg.line.gripperType + "  palletEnabled=" + $cfg.line.palletEnabled)
        Emit ("  eman.host: " + $cfg.eman.host)
        Emit ("  nc_root  : " + $cfg.nc_server.root_path)
    } catch {
        Emit ("  配置解析失败: " + $_.Exception.Message)
    }
} else {
    Emit "  未找到 config.json（可用 -AppDir 指定目录）"
}

# ---------- 5. 编译环境 ----------
Emit ""
Emit "--- 5. 编译环境（决定能否现场改码即时编译）---"
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (Test-Path $csc) {
    Emit "  csc.exe  : 有（系统自带，可现场编译 C#）"
} else {
    Emit "  csc.exe  : 无"
}
$msb = Get-Command msbuild.exe -ErrorAction SilentlyContinue
if ($msb) { Emit ("  msbuild  : " + $msb.Source) } else { Emit "  msbuild  : 未找到" }
$dn = Get-Command dotnet.exe -ErrorAction SilentlyContinue
if ($dn) { Emit ("  dotnet   : " + $dn.Source) } else { Emit "  dotnet   : 未找到" }

# ---------- 6. 数据库 ----------
Emit ""
Emit "--- 6. 数据库 ---"
$sqlcmd = (Get-Command sqlcmd.exe -ErrorAction SilentlyContinue).Source
if (-not $sqlcmd) {
    $alt = 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\180\Tools\Binn\sqlcmd.exe'
    if (Test-Path $alt) { $sqlcmd = $alt }
}

if (-not $sqlcmd) {
    Emit "  未找到 sqlcmd，跳过数据库检查"
} else {
    Emit ("  sqlcmd: " + $sqlcmd)

    $q1 = "set nocount on; select top 15 id, log_level, left(log_content,90) as msg, convert(varchar(19),created_at,120) as t from process_log order by id desc"
    Emit "  [process_log 最近 15 条]"
    $r1 = & $sqlcmd -S $DbServer -U $DbUser -P $DbPass -d $DbName -C -W -s "|" -Q $q1 2>&1
    foreach ($line in $r1) { Emit ("    " + $line) }

    $q2 = "set nocount on; select id, step_name, status, priority from work_flow order by priority desc, id asc"
    Emit "  [work_flow 步骤]"
    $r2 = & $sqlcmd -S $DbServer -U $DbUser -P $DbPass -d $DbName -C -W -s "|" -Q $q2 2>&1
    foreach ($line in $r2) { Emit ("    " + $line) }

    $q3 = "set nocount on; select count(*) as log_count from process_log"
    $r3 = & $sqlcmd -S $DbServer -U $DbUser -P $DbPass -d $DbName -C -W -s "|" -Q $q3 2>&1
    Emit "  [process_log 总数]"
    foreach ($line in $r3) { Emit ("    " + $line) }
}

# ---------- 7. 子程序文件时间 ----------
Emit ""
Emit "--- 7. 子程序文件（确认现场跑的是哪一版）---"
if ($AppDir) {
    $subs = Join-Path $AppDir 'subs'
    if (Test-Path $subs) {
        Get-ChildItem $subs -Filter *.exe -ErrorAction SilentlyContinue | Sort-Object Name | ForEach-Object {
            Emit ("  {0,-26} {1}  {2} 字节" -f $_.Name, $_.LastWriteTime.ToString('MM-dd HH:mm'), $_.Length)
        }
    } else {
        Emit "  未找到 subs 目录"
    }
}

# ---------- 8. 设备连通性 ----------
Emit ""
Emit "--- 8. 设备连通性 ---"
$targets = @(
    @{ ip = '192.168.125.201'; desc = '本机serverIp' },
    @{ ip = '192.168.101.10';  desc = 'PLC' },
    @{ ip = '192.168.101.51';  desc = '机床3' },
    @{ ip = '192.168.101.52';  desc = '机床2' },
    @{ ip = '192.168.101.53';  desc = '机床1' },
    @{ ip = '192.168.101.71';  desc = '扫码枪' }
)
foreach ($t in $targets) {
    $ok = Test-Connection -ComputerName $t.ip -Count 1 -Quiet -ErrorAction SilentlyContinue
    $txt = if ($ok) { '可达' } else { '不可达' }
    Emit ("  {0,-18} {1,-12} {2}" -f $t.ip, $t.desc, $txt)
}

Emit ""
Emit ("=" * 58)
Emit "报告结束，请整段复制回传"

$reportFile = Join-Path (Get-Location).Path ("field-report-" + (Get-Date -Format 'yyyyMMdd-HHmm') + ".txt")
try {
    $out | Set-Content -Path $reportFile -Encoding UTF8
    Write-Host ""
    Write-Host ("报告已同时保存到: " + $reportFile) -ForegroundColor Green
} catch {
    Write-Host ""
    Write-Host "报告保存失败，请直接复制上面的控制台输出。" -ForegroundColor Yellow
}
Write-Host ""
Read-Host "按回车键退出"
