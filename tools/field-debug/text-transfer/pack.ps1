# 把任意文件转成「纯文本可粘贴」的 base64 分片
# 用途：内部平台只能传文字时，用文本形式把文件送进/送出隔离环境
param(
    [Parameter(Mandatory=$true)][string]$Path,
    [int]$ChunkSize = 2000,
    [string]$OutFile = ''
)

$full = (Resolve-Path $Path).Path
$bytes = [IO.File]::ReadAllBytes($full)
$b64 = [Convert]::ToBase64String($bytes)
$name = Split-Path $full -Leaf
$hash = (Get-FileHash $full -Algorithm SHA256).Hash

$parts = New-Object System.Collections.ArrayList
for ($i = 0; $i -lt $b64.Length; $i += $ChunkSize) {
    $len = [Math]::Min($ChunkSize, $b64.Length - $i)
    [void]$parts.Add($b64.Substring($i, $len))
}

if (-not $OutFile) {
    $OutFile = Join-Path (Split-Path $full -Parent) ($name + '.b64.txt')
}

$lines = New-Object System.Collections.ArrayList
[void]$lines.Add("# 文件: $name")
[void]$lines.Add("# 原始大小: $($bytes.Length) 字节")
[void]$lines.Add("# SHA256: $hash")
[void]$lines.Add("# 分片: $($parts.Count) 片，每片不超过 $ChunkSize 字符")
[void]$lines.Add("#")
[void]$lines.Add("# 【还原方法】")
[void]$lines.Add("# 1. 把下面所有 PART 分片内容合并保存成一个 txt，例如 $name.txt")
[void]$lines.Add("# 2. 在该 txt 所在目录打开 PowerShell，执行下面这两行命令：")
[void]$lines.Add("#")
[void]$lines.Add("#    `$t = ((Get-Content '.\$name.txt') | Where-Object { `$_ -notmatch '^[#=]' }) -join ''")
[void]$lines.Add("#    [IO.File]::WriteAllBytes('$name',[Convert]::FromBase64String(`$t))")
[void]$lines.Add("#")
[void]$lines.Add("# 原理：base64 字符集里不含 #，而尾部的 = 只出现在最后一片末尾，")
[void]$lines.Add("# 因此所有 # 注释行与 ===== 标记行都会被自动剔除。")
[void]$lines.Add("")

$n = 0
foreach ($p in $parts) {
    $n++
    [void]$lines.Add("===== PART $n/$($parts.Count) =====")
    [void]$lines.Add($p)
}

$lines | Set-Content -Path $OutFile -Encoding UTF8

Write-Host ""
Write-Host "已生成: $OutFile" -ForegroundColor Green
Write-Host ("  文件   : {0}" -f $name)
Write-Host ("  大小   : {0} 字节" -f $bytes.Length)
Write-Host ("  分片   : {0} 片（每片 {1} 字符）" -f $parts.Count, $ChunkSize)
Write-Host ("  SHA256 : {0}" -f $hash)
Write-Host ""
Write-Host "如果平台单条消息放不下，可加大 -ChunkSize 或按 PART 分多条发送。" -ForegroundColor Cyan
Write-Host ""
Read-Host "按回车键退出"
