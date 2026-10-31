# 用 WPF GifBitmapDecoder（= FramePlayer 的解码路径）验证 GIF：帧数/尺寸/每帧延时
param([string]$p)
Add-Type -AssemblyName PresentationCore
$dec = New-Object System.Windows.Media.Imaging.GifBitmapDecoder((New-Object System.Uri($p)), [System.Windows.Media.Imaging.BitmapCreateOptions]::None, [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
$delays = @($dec.Frames | ForEach-Object { $d = $_.Metadata.GetQuery('/grctlext/Delay'); if ($d -ne $null) { [int]$d * 10 } else { -1 } })
Write-Output ("frames={0} size={1}x{2} delays(ms)={3}..{4} loop={5}ms" -f $dec.Frames.Count, $dec.Frames[0].PixelWidth, $dec.Frames[0].PixelHeight, ($delays | Measure-Object -Minimum).Minimum, ($delays | Measure-Object -Maximum).Maximum, ($delays | Measure-Object -Sum).Sum)
