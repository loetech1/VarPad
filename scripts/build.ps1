param([switch]$SkipTests, [switch]$Installer, [string]$OutputDirectory = 'dist')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath $framework)) { $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
$compiler = Join-Path $framework 'csc.exe'
$out = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
$build = Join-Path $root 'build'
New-Item -ItemType Directory -Force -Path $out, $build | Out-Null

# A simple, code-drawn app icon, with no external assets or build dependencies.
Add-Type -AssemblyName System.Drawing
$bitmap = New-Object System.Drawing.Bitmap 64,64
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::FromArgb(37,41,39))
$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(157,232,170)),5
$graphics.DrawRectangle($pen, 8, 8, 47, 47)
$graphics.DrawLine($pen, 21, 26, 43, 26)
$graphics.DrawLine($pen, 21, 38, 43, 38)
$png = New-Object System.IO.MemoryStream
$bitmap.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
$iconFile = Join-Path $build 'app.ico'
$stream = [System.IO.File]::Create($iconFile)
$writer = New-Object System.IO.BinaryWriter $stream
$writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]1)
$writer.Write([byte]64); $writer.Write([byte]64); $writer.Write([byte]0); $writer.Write([byte]0)
$writer.Write([UInt16]1); $writer.Write([UInt16]32); $writer.Write([UInt32]$png.Length); $writer.Write([UInt32]22)
$writer.Write($png.ToArray()); $writer.Dispose(); $png.Dispose(); $pen.Dispose(); $graphics.Dispose(); $bitmap.Dispose()

$refs = @('System.dll','System.Core.dll','System.Xml.dll','System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll') | ForEach-Object { '/r:' + (Join-Path $framework $_) }
$sources = @('App.cs','Calculator.cs','InlineAnswer.cs','Storage.cs') | ForEach-Object { Join-Path $root "src\$_" }
$appArgs = @('/nologo','/target:winexe','/platform:anycpu','/optimize+',('/out:' + (Join-Path $out 'VarPad.exe')),('/win32icon:' + $iconFile),('/win32manifest:' + (Join-Path $root 'src\app.manifest')),('/resource:' + (Join-Path $root 'src\MainWindow.xaml') + ',MainWindow.xaml'),('/resource:' + (Join-Path $root 'src\Help.txt') + ',Help.txt'),('/resource:' + $iconFile + ',app.ico')) + $refs + $sources
& $compiler @appArgs
if ($LASTEXITCODE -ne 0) { throw 'App compilation failed.' }
Copy-Item -LiteralPath (Join-Path $root 'src\VarPad.exe.config') -Destination $out
Copy-Item -LiteralPath $iconFile -Destination $out

if (-not $SkipTests) {
    $testArgs = @('/nologo','/target:exe',('/out:' + (Join-Path $build 'Tests.exe')),'/r:System.Core.dll','/r:System.Xml.dll',(Join-Path $root 'src\Calculator.cs'),(Join-Path $root 'src\InlineAnswer.cs'),(Join-Path $root 'src\Storage.cs'),(Join-Path $root 'tests\Tests.cs'))
    & $compiler @testArgs
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & (Join-Path $build 'Tests.exe') (Join-Path $build 'test-notes')
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}

if ($Installer) {
$payload = @('VarPad.exe','VarPad.exe.config','app.ico')
$hashes = $payload | ForEach-Object { (Get-FileHash -LiteralPath (Join-Path $out $_) -Algorithm SHA256).Hash.ToLowerInvariant() }
$hashFile = Join-Path $build 'sha256.txt'
[System.IO.File]::WriteAllText($hashFile, ($hashes -join "`n"), (New-Object System.Text.UTF8Encoding $false))
$resources = $payload | ForEach-Object { '/resource:' + (Join-Path $out $_) + ',Payload.' + $_ }
$setupArgs = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll',('/out:' + (Join-Path $out 'VarPad-Setup.exe')),('/win32icon:' + $iconFile),('/win32manifest:' + (Join-Path $root 'src\app.manifest')),('/resource:' + $hashFile + ',Payload.sha256.txt')) + $resources + (Join-Path $root 'installer\Setup.cs')
& $compiler @setupArgs
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
$verify = Start-Process -FilePath (Join-Path $out 'VarPad-Setup.exe') -ArgumentList '--verify-payload' -WindowStyle Hidden -Wait -PassThru
if ($verify.ExitCode -ne 0) { throw 'Installer payload verification failed.' }
Write-Output "Built: $out\VarPad-Setup.exe"
}
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination (Join-Path $out 'README.txt')
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $out 'LICENSE.txt')
$zip = Join-Path $out 'VarPad-Portable.zip'
Compress-Archive -LiteralPath (Join-Path $out 'VarPad.exe'),(Join-Path $out 'VarPad.exe.config'),(Join-Path $out 'app.ico'),(Join-Path $out 'README.txt'),(Join-Path $out 'LICENSE.txt') -DestinationPath $zip -Force
Write-Output "Built standalone app: $out\VarPad.exe"
