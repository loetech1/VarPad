$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath $framework)) { $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
$compiler = Join-Path $framework 'csc.exe'
$build = Join-Path $root 'build'
$refs = @('System.dll','System.Core.dll','System.Xml.dll','System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll') | ForEach-Object { '/r:' + (Join-Path $framework $_) }
$sources = @('App.cs','Calculator.cs','Completion.cs','InputGrouping.cs','InlineAnswer.cs','Storage.cs') | ForEach-Object { Join-Path $root "src\$_" }
$arguments = @('/nologo','/target:exe','/main:LayoutChecks',('/out:' + (Join-Path $build 'LayoutChecks.exe')),('/resource:' + (Join-Path $root 'src\MainWindow.xaml') + ',MainWindow.xaml'),('/resource:' + (Join-Path $root 'src\Help.txt') + ',Help.txt'),('/resource:' + (Join-Path $build 'app.ico') + ',app.ico')) + $refs + $sources + (Join-Path $root 'tests\LayoutChecks.cs')
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Layout check compilation failed.' }
& (Join-Path $build 'LayoutChecks.exe') (Join-Path $build ('layout-notes-' + [guid]::NewGuid().ToString('N'))) (Join-Path $build 'layout-preview.png')
if ($LASTEXITCODE -ne 0) { throw 'Layout checks failed.' }
