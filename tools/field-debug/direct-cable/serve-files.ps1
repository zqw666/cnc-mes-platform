<#
    serve-files.ps1
    在本机(开发机)起一个极简 HTTP 文件下载服务，让工控机用浏览器把文件拉过去。

    为什么用这个：现场禁 U 盘、内部平台只能传文字，但网线一通就是一条私有链路。
    工控机作为「客户端」访问网页，不需要它开任何端口、不需要账号、不改任何设置。

    用法：
        .\serve-files.ps1                                  # 共享当前目录，端口 8000
        .\serve-files.ps1 -Root "D:\deploy"                # 指定要推送的目录
        .\serve-files.ps1 -Root "D:\deploy" -Port 8080     # 换端口

    指定目录后，直接在工控机浏览器打开脚本提示的 URL 即可看到目录列表并下载。
    支持大文件（流式发送），Ctrl+C 停止。

    注意：不需要管理员权限。首次运行 Windows 防火墙可能弹窗，选「允许」。
#>
[CmdletBinding()]
param(
    [string]$Root = (Get-Location).Path,
    [int]$Port = 8000,
    [string]$BindIp = ''
)

$ErrorActionPreference = 'Stop'
function Pause-Exit { if (-not [Console]::IsInputRedirected) { [void](Read-Host) } }

# ---------- 解析共享目录 ----------
if (-not (Test-Path -LiteralPath $Root)) {
    Write-Host "目录不存在: $Root" -ForegroundColor Red
    Pause-Exit
    exit 1
}
$Root = (Resolve-Path -LiteralPath $Root).Path

# ---------- 选绑定 IP：优先有线口上的 125/101 网段地址 ----------
function Get-BindCandidates {
    $list = @()
    $wired = @(Get-NetAdapter -Physical -ErrorAction SilentlyContinue | Where-Object { $_.MediaType -eq '802.3' -and $_.Status -eq 'Up' })
    foreach ($a in $wired) {
        Get-NetIPAddress -InterfaceIndex $a.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue |
            Where-Object { $_.IPAddress -notlike '169.254.*' } |
            ForEach-Object { $list += [pscustomobject]@{ IP = $_.IPAddress; If = $a.Name; Wired = $true } }
    }
    # 兜底：任何非回环地址
    Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
        Where-Object { $_.IPAddress -ne '127.0.0.1' -and $_.IPAddress -notlike '169.254.*' } |
        ForEach-Object {
            $ipv = $_.IPAddress
            $alias = $_.InterfaceAlias
            if (-not ($list | Where-Object { $_.IP -eq $ipv })) {
                $list += [pscustomobject]@{ IP = $ipv; If = $alias; Wired = $false }
            }
        }
    # 排序：有线优先，其次 125/101 段
    $list | Sort-Object @{ Expression = { if (-not $_.Wired) { 2 } elseif ($_.IP -like '192.168.*') { 0 } else { 1 } } }
}

if (-not $BindIp) {
    $cand = @(Get-BindCandidates)
    $pick = $cand | Where-Object { $_.Wired -and ($_.IP -like '192.168.125.*' -or $_.IP -like '192.168.101.*') } | Select-Object -First 1
    if (-not $pick) { $pick = $cand | Where-Object { $_.Wired } | Select-Object -First 1 }
    if (-not $pick) { $pick = $cand | Select-Object -First 1 }
    if ($pick) { $BindIp = $pick.IP }
}

if (-not $BindIp) {
    Write-Host '没找到可用的本机 IPv4 地址，请先接好网线并配好 IP。' -ForegroundColor Red
    Pause-Exit
    exit 1
}

# ---------- 关掉 nagling / 建监听 ----------
$listener = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Parse($BindIp), $Port)
try {
    $listener.Start()
} catch {
    Write-Host ("端口 {0} 绑定失败：{1}" -f $Port, $_.Exception.Message) -ForegroundColor Red
    Write-Host '换个端口试试：.\serve-files.ps1 -Port 8081' -ForegroundColor Yellow
    Pause-Exit
    exit 1
}

# ---------- 放行防火墙（尽力而为，非管理员会失败，不影响使用） ----------
try {
    $rule = Get-NetFirewallRule -DisplayName "field-file-serve-$Port" -ErrorAction SilentlyContinue
    if (-not $rule) {
        New-NetFirewallRule -DisplayName "field-file-serve-$Port" -Direction Inbound -Action Allow -Protocol TCP -LocalPort $Port -Profile Any -ErrorAction Stop | Out-Null
        Write-Host ("已添加防火墙放行规则（TCP {0}）" -f $Port) -ForegroundColor DarkGray
    }
} catch {
    Write-Host '未能自动放行防火墙（大概率不是管理员）。若工控机连不上，请手工放行该端口或换端口。' -ForegroundColor DarkYellow
}

$mime = @{
    '.txt'  = 'text/plain; charset=utf-8'
    '.log'  = 'text/plain; charset=utf-8'
    '.md'   = 'text/plain; charset=utf-8'
    '.json' = 'application/json; charset=utf-8'
    '.xml'  = 'application/xml; charset=utf-8'
    '.html' = 'text/html; charset=utf-8'
    '.htm'  = 'text/html; charset=utf-8'
    '.css'  = 'text/css; charset=utf-8'
    '.js'   = 'application/javascript; charset=utf-8'
    '.png'  = 'image/png'
    '.jpg'  = 'image/jpeg'
    '.jpeg' = 'image/jpeg'
    '.gif'  = 'image/gif'
    '.svg'  = 'image/svg+xml'
    '.ico'  = 'image/x-icon'
    '.pdf'  = 'application/pdf'
    '.zip'  = 'application/zip'
    '.7z'   = 'application/x-7z-compressed'
    '.rar'  = 'application/vnd.rar'
    '.exe'  = 'application/octet-stream'
    '.dll'  = 'application/octet-stream'
    '.pdb'  = 'application/octet-stream'
    '.config' = 'text/plain; charset=utf-8'
    '.bat'  = 'application/octet-stream'
    '.cmd'  = 'application/octet-stream'
    '.ps1'  = 'text/plain; charset=utf-8'
    '.csv'  = 'text/csv; charset=utf-8'
    '.xlsx' = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'
    '.docx' = 'application/vnd.openxmlformats-officedocument.wordprocessingml.document'
}
function Get-Mime([string]$Path) {
    $e = [IO.Path]::GetExtension($Path).ToLower()
    if ($mime.ContainsKey($e)) { return $mime[$e] }
    return 'application/octet-stream'
}

function Write-RespLine {
    param($Stream, [string]$Text)
    $b = [Text.Encoding]::ASCII.GetBytes($Text + "`r`n")
    $Stream.Write($b, 0, $b.Length)
}

function Get-DirHtml {
    param([string]$FullPath, [string]$UrlPath)
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append('<!doctype html><html><head><meta charset="utf-8"><title>文件列表</title>')
    [void]$sb.Append('<style>body{font-family:Consolas,"Microsoft YaHei",monospace;background:#f7f8fa;color:#1f2328;margin:24px}')
    [void]$sb.Append('h1{font-size:18px;border-bottom:2px solid #d0d7de;padding-bottom:8px}')
    [void]$sb.Append('a{color:#0969da;text-decoration:none}a:hover{text-decoration:underline}')
    [void]$sb.Append('table{border-collapse:collapse;width:100%;margin-top:12px;background:#fff}')
    [void]$sb.Append('th,td{text-align:left;padding:8px 12px;border-bottom:1px solid #eaecef;font-size:14px}')
    [void]$sb.Append('th{background:#f6f8fa;color:#57606a;font-weight:600}')
    [void]$sb.Append('.sz{color:#57606a;white-space:nowrap}</style></head><body>')
    [void]$sb.Append('<h1>文件列表 &mdash; ' + $UrlPath + '</h1><table><tr><th>名称</th><th>大小</th><th>修改时间</th></tr>')

    if ($UrlPath -ne '/') {
        $parent = ($UrlPath.TrimEnd('/') -split '/')[0..(($UrlPath.TrimEnd('/') -split '/').Count - 2)]
        $pu = '/' + (($parent -join '/').Trim('/'))
        if ($pu -ne '/') { $pu = $pu + '/' }
        [void]$sb.Append('<tr><td colspan="3"><a href="' + $pu + '">.. 上级目录</a></td></tr>')
    }

    foreach ($d in (Get-ChildItem -LiteralPath $FullPath -Directory -ErrorAction SilentlyContinue | Sort-Object Name)) {
        $href = ($UrlPath.TrimEnd('/')) + '/' + [uri]::EscapeDataString($d.Name) + '/'
        [void]$sb.Append('<tr><td><a href="' + $href + '">[DIR] ' + [System.Net.WebUtility]::HtmlEncode($d.Name) + '/</a></td><td class="sz">-</td><td class="sz">' + $d.LastWriteTime.ToString('yyyy-MM-dd HH:mm') + '</td></tr>')
    }
    foreach ($f in (Get-ChildItem -LiteralPath $FullPath -File -ErrorAction SilentlyContinue | Sort-Object Name)) {
        $href = ($UrlPath.TrimEnd('/')) + '/' + [uri]::EscapeDataString($f.Name)
        $size = if ($f.Length -ge 1MB) { '{0:N2} MB' -f ($f.Length / 1MB) } elseif ($f.Length -ge 1KB) { '{0:N1} KB' -f ($f.Length / 1KB) } else { "$($f.Length) B" }
        [void]$sb.Append('<tr><td><a href="' + $href + '">' + [System.Net.WebUtility]::HtmlEncode($f.Name) + '</a></td><td class="sz">' + $size + '</td><td class="sz">' + $f.LastWriteTime.ToString('yyyy-MM-dd HH:mm') + '</td></tr>')
    }
    [void]$sb.Append('</table></body></html>')
    return $sb.ToString()
}

# ---------- 启动提示 ----------
$urls = @("http://${BindIp}:$Port/")

try { Clear-Host } catch { }
Write-Host ('=' * 66) -ForegroundColor Cyan
Write-Host '  文件推送服务已启动' -ForegroundColor Cyan
Write-Host ('=' * 66) -ForegroundColor Cyan
Write-Host ("  共享目录 : {0}" -f $Root) -ForegroundColor White
Write-Host ("  监听      : {0}:{1}" -f $BindIp, $Port) -ForegroundColor White
Write-Host ''
Write-Host '  >>> 在工控机上打开浏览器，访问：' -ForegroundColor Yellow
foreach ($u in $urls) { Write-Host ("        " + $u) -ForegroundColor Yellow }
Write-Host ''
Write-Host '  按 Ctrl+C 停止服务。' -ForegroundColor DarkGray
Write-Host ('=' * 66) -ForegroundColor Cyan

# ---------- 主循环 ----------
while ($true) {
    $client = $null
    try {
        $client = $listener.AcceptTcpClient()
        $client.ReceiveTimeout = 8000
        $client.SendTimeout = 120000
        $stream = $client.GetStream()

        $reader = New-Object System.IO.StreamReader($stream, [Text.Encoding]::ASCII, $false, 4096, $true)
        $reqLine = $reader.ReadLine()
        if (-not $reqLine) { $client.Close(); continue }
        # 丢弃剩余请求头
        while ($true) {
            $h = $reader.ReadLine()
            if ($null -eq $h -or $h -eq '') { break }
        }

        $bits = $reqLine.Split(' ')
        $method = $bits[0]
        $rawPath = if ($bits.Length -gt 1) { $bits[1] } else { '/' }
        $rawPath = ($rawPath -split '\?')[0]
        $decoded = [uri]::UnescapeDataString($rawPath).Replace('/', '\')
        $rel = $decoded.TrimStart('\')

        $full = Join-Path $Root $rel
        $full = [IO.Path]::GetFullPath($full)

        # 防目录穿越
        if (-not $full.StartsWith($Root, [StringComparison]::OrdinalIgnoreCase)) {
            Write-RespLine $stream 'HTTP/1.1 403 Forbidden'
            Write-RespLine $stream "Content-Length: 9"
            Write-RespLine $stream 'Connection: close'
            Write-RespLine $stream ''
            $b = [Text.Encoding]::ASCII.GetBytes('forbidden')
            $stream.Write($b, 0, $b.Length)
            Write-Host ("  [403] {0}" -f $rawPath) -ForegroundColor Red
            $client.Close(); continue
        }

        $urlPath = '/' + ($rel -replace '\\', '/')
        if ($urlPath -ne '/' -and -not $urlPath.EndsWith('/') -and (Test-Path -LiteralPath $full -PathType Container)) {
            $urlPath = $urlPath + '/'
        }

        if (Test-Path -LiteralPath $full -PathType Container) {
            $html = Get-DirHtml -FullPath $full -UrlPath $urlPath
            $bytes = [Text.Encoding]::UTF8.GetBytes($html)
            Write-RespLine $stream 'HTTP/1.1 200 OK'
            Write-RespLine $stream 'Content-Type: text/html; charset=utf-8'
            Write-RespLine $stream ("Content-Length: {0}" -f $bytes.Length)
            Write-RespLine $stream 'Cache-Control: no-store'
            Write-RespLine $stream 'Connection: close'
            Write-RespLine $stream ''
            $stream.Write($bytes, 0, $bytes.Length)
            Write-Host ("  [目录] {0}" -f $urlPath) -ForegroundColor Gray
        }
        elseif (Test-Path -LiteralPath $full -PathType Leaf) {
            $fi = Get-Item -LiteralPath $full
            Write-RespLine $stream 'HTTP/1.1 200 OK'
            Write-RespLine $stream ("Content-Type: {0}" -f (Get-Mime $full))
            Write-RespLine $stream ("Content-Length: {0}" -f $fi.Length)
            Write-RespLine $stream ("Content-Disposition: attachment; filename=""{0}""" -f [uri]::EscapeDataString($fi.Name))
            Write-RespLine $stream 'Connection: close'
            Write-RespLine $stream ''
            $fs = [IO.File]::Open($full, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
            try { $fs.CopyTo($stream, 1048576) } finally { $fs.Dispose() }
            Write-Host ("  [下载] {0}  ({1:N1} KB)" -f $fi.Name, ($fi.Length / 1KB)) -ForegroundColor Green
        }
        else {
            $body = [Text.Encoding]::UTF8.GetBytes('404 not found: ' + $urlPath)
            Write-RespLine $stream 'HTTP/1.1 404 Not Found'
            Write-RespLine $stream 'Content-Type: text/plain; charset=utf-8'
            Write-RespLine $stream ("Content-Length: {0}" -f $body.Length)
            Write-RespLine $stream 'Connection: close'
            Write-RespLine $stream ''
            $stream.Write($body, 0, $body.Length)
            Write-Host ("  [404] {0}" -f $urlPath) -ForegroundColor DarkYellow
        }

        try { $reader.Dispose() } catch { }
        $client.Close()
    }
    catch {
        if ($client) { try { $client.Close() } catch { } }
    }
}
