# 把现场回传的 base64 文本还原成文件（自动剔除注释与分片标记，并校验 SHA256）
param(
    [Parameter(Mandatory=$true)][string]$InFile,
    [Parameter(Mandatory=$true)][string]$OutFile,
    [string]$ExpectSha256 = ''
)

if (-not (Test-Path $InFile)) {
    Write-Host ("找不到输入文件: " + $InFile) -ForegroundColor Red
    Read-Host "按回车键退出"
    exit 1
}

$lines = Get-Content -Path $InFile
$b64 = ($lines | Where-Object { $_ -notmatch '^[#=]' }) -join ''
$b64 = $b64 -replace '\s', ''

if ([string]::IsNullOrWhiteSpace($b64)) {
    Write-Host "文件里没有找到 base64 内容。" -ForegroundColor Red
    Read-Host "按回车键退出"
    exit 1
}

$bytes = $null
try {
    $bytes = [Convert]::FromBase64String($b64)
} catch {
    Write-Host "按行过滤后解码失败，改用严格字符过滤重试..." -ForegroundColor Yellow
    try {
        $b64b = (Get-Content -Path $InFile -Raw) -replace '[^A-Za-z0-9+/=]', ''
        $bytes = [Convert]::FromBase64String($b64b)
    } catch {
        Write-Host ("base64 解码失败: " + $_.Exception.Message) -ForegroundColor Red
        Write-Host "常见原因：分片漏粘、或粘贴过程混入了其它字符。" -ForegroundColor Yellow
        Read-Host "按回车键退出"
        exit 1
    }
}

$dir = Split-Path $OutFile -Parent
if ($dir -and -not (Test-Path $dir)) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}
[IO.File]::WriteAllBytes($OutFile, $bytes)

$hash = (Get-FileHash $OutFile -Algorithm SHA256).Hash

Write-Host ""
Write-Host ("已还原: " + (Resolve-Path $OutFile).Path) -ForegroundColor Green
Write-Host ("  大小   : {0} 字节" -f $bytes.Length)
Write-Host ("  SHA256 : {0}" -f $hash)

if ($ExpectSha256) {
    if ($hash -eq $ExpectSha256.ToUpper()) {
        Write-Host "  校验   : 通过，文件完整" -ForegroundColor Green
    } else {
        Write-Host "  校验   : 不一致！文件可能不完整，请核对分片" -ForegroundColor Red
    }
}

Write-Host ""
Read-Host "按回车键退出"
