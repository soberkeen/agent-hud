$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc -nologo -unsafe -out:tools\bcrypt-unsafe.exe tools\bcrypt-unsafe.cs
