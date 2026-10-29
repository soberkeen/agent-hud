$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $framework 'csc.exe'
$wpf = Join-Path $framework 'WPF'
$src = Join-Path $PSScriptRoot '..\agent-hud.cs'
& $csc -nologo -debug:full -target:winexe -out:hdr-dbg.exe `
    -r:"$wpf\PresentationFramework.dll" -r:"$wpf\PresentationCore.dll" `
    -r:"$wpf\WindowsBase.dll" -r:"$framework\System.Xaml.dll" `
    -r:"$framework\System.Web.Extensions.dll" `
    -r:"$framework\System.Windows.Forms.dll" -r:"$framework\System.Drawing.dll" `
    $src
Write-Host done
