# build.ps1 — 编译 agent-hud.exe 与 AgentHUD-Setup.exe（用系统内置 csc，C#5，无需任何 SDK）
# 用法:
#   .\build.ps1               全量构建（app + 安装器）
#   .\build.ps1 -SetupOnly    只重打安装器（把“已签名”的 agent-hud.exe 嵌进去，配合 sign.ps1 用）
param(
    [switch]$SetupOnly
)
$ErrorActionPreference = 'Stop'

$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $framework 'csc.exe'
$wpf = Join-Path $framework 'WPF'
$icon = Join-Path $PSScriptRoot 'agent-hud.ico'
$src = Join-Path $PSScriptRoot 'agent-hud.cs'
$manifest = Join-Path $PSScriptRoot 'app.manifest'
$setupSrc = Join-Path $PSScriptRoot 'setup.cs'
$setupManifest = Join-Path $PSScriptRoot 'setup.manifest'
$appExe = Join-Path $PSScriptRoot 'agent-hud.exe'
$setupExe = Join-Path $PSScriptRoot 'AgentHUD-Setup.exe'

if (-not (Test-Path $csc)) { throw "未找到内置编译器: $csc" }
if ($SetupOnly -and -not (Test-Path $appExe)) { throw "缺少 agent-hud.exe：-SetupOnly 只重打包，请先全量构建" }

if (-not $SetupOnly) {
    Write-Host '== 编译 agent-hud.exe =='
$iconDfy = Join-Path $PSScriptRoot 'icon-dafeiyu.png'
$iconZc  = Join-Path $PSScriptRoot 'icon-zcode.png'
$resDfy  = '-res:{0},Icon.Dafeiyu.png' -f $iconDfy
$resZc   = '-res:{0},Icon.Zcode.png'   -f $iconZc
# 动画精灵图不再内嵌主程序（外置 assets 文件夹）；安装器以 Asset. 前缀内嵌并在安装时释放
$resAssets = @()
Get-ChildItem $PSScriptRoot -Filter 'anim-*.png' | ForEach-Object {
    $resAssets += ('-res:{0},Asset.{1}' -f $_.FullName, $_.Name)
}
Get-ChildItem $PSScriptRoot -Filter 'icon-*.png' | ForEach-Object {
    $resAssets += ('-res:{0},Asset.{1}' -f $_.FullName, $_.Name)
}
Get-ChildItem $PSScriptRoot -Filter 'anim-*.gif' | ForEach-Object {
    $resAssets += ('-res:{0},Asset.{1}' -f $_.FullName, $_.Name)
}
& $csc -nologo -target:winexe -out:$appExe -win32icon:$icon -win32manifest:$manifest `
    -r:"$wpf\PresentationFramework.dll" -r:"$wpf\PresentationCore.dll" `
    -r:"$wpf\WindowsBase.dll" -r:"$framework\System.Xaml.dll" `
    -r:"$framework\System.Web.Extensions.dll" `
    -r:"$framework\System.Windows.Forms.dll" -r:"$framework\System.Drawing.dll" `
    $resDfy $resZc $src
    if ($LASTEXITCODE -ne 0) { throw "hdr-toggle.exe 编译失败 (exit $LASTEXITCODE)" }
}

Write-Host '== 编译 AgentHUD-Setup.exe（内嵌 agent-hud.exe + assets） =='
$resArg = '-res:{0},HDR.Toggle.app.exe' -f $appExe
& $csc -nologo -target:winexe -out:$setupExe -win32icon:$icon -win32manifest:$setupManifest `
    -r:"$framework\Microsoft.CSharp.dll" -r:"$framework\System.dll" `
    -r:"$framework\System.Windows.Forms.dll" -r:"$framework\System.Drawing.dll" `
    $resArg $resAssets $setupSrc
if ($LASTEXITCODE -ne 0) { throw "安装器编译失败 (exit $LASTEXITCODE)" }

Get-Item $appExe, $setupExe | ForEach-Object { '{0}  {1:N0} KB' -f $_.Name, ($_.Length / 1KB) }
Write-Host '完成。发布前建议运行 .\sign.ps1 做代码签名（详见 发布说明.md）。'
