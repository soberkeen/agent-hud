# sign.ps1 — 对构建产物做 Authenticode 代码签名（防杀软/SmartScreen 误报的根本手段）
# 用法（三选一）:
#   .\sign.ps1 -PfxPath <证书.pfx> -PfxPassword <密码>
#   .\sign.ps1 -Thumbprint <证书指纹>        # 用当前用户证书库里的证书（certmgr.msc 可看指纹）
#   .\sign.ps1                               # 自动在当前用户库找“代码签名”用途的证书
# 说明: 需要先有代码签名证书（OV/EV 均可，见 发布说明.md）。签名会加时间戳，证书过期后签名仍有效。
param(
    [string]$PfxPath,
    [string]$PfxPassword,
    [string]$Thumbprint
)
$ErrorActionPreference = 'Stop'

$targets = @(
    (Join-Path $PSScriptRoot 'hdr-toggle.exe'),
    (Join-Path $PSScriptRoot 'HDR开关安装器.exe')
)

$cert = $null
if ($PfxPath) {
    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($PfxPath, $PfxPassword)
}
elseif ($Thumbprint) {
    $cert = Get-Item ("Cert:\CurrentUser\My\" + $Thumbprint) -ErrorAction Stop
}
else {
    $cert = Get-ChildItem Cert:\CurrentUser\My |
        Where-Object { $_.HasPrivateKey -and ($_.EnhancedKeyUsageList.FriendlyName -contains 'Code Signing') } |
        Select-Object -First 1
}
if (-not $cert) {
    throw "未找到代码签名证书：请用 -PfxPath/-PfxPassword 或 -Thumbprint 指定（证书用途须为“代码签名”）"
}

foreach ($f in $targets) {
    if (-not (Test-Path $f)) { throw "产物缺失: $f（先运行 build.ps1）" }
    $r = Set-AuthenticodeSignature -FilePath $f -Certificate $cert -TimestampServer 'http://timestamp.digicert.com'
    '{0}: {1}' -f (Split-Path $f -Leaf), $r.Status
}
Write-Host ''
Write-Host '已完成签名。分发顺序建议：'
Write-Host '  1. build.ps1 → 2. sign.ps1（签 hdr-toggle.exe）→ 3. build.ps1 -SetupOnly（把已签名的 app 打进安装器）'
Write-Host '  → 4. sign.ps1（再签安装器外壳）→ 分发 HDR开关安装器.exe'
