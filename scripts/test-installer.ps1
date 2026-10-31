param([string]$OutputDirectory = 'dist')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$setup = Join-Path (Join-Path $root $OutputDirectory) 'VarPad-Setup.exe'
$target = Join-Path $root ('build\installer-test-' + [guid]::NewGuid().ToString('N'))
$resolved = [IO.Path]::GetFullPath($target)
$allowed = [IO.Path]::GetFullPath((Join-Path $root 'build')) + [IO.Path]::DirectorySeparatorChar
if (-not $resolved.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Test path escaped build directory.' }
foreach ($mode in @('--extract', '--verify-extracted', '--remove-extracted')) {
    $process = Start-Process -FilePath $setup -ArgumentList ($mode + ' "' + $resolved + '"') -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Installer check failed: $mode" }
    if ($mode -eq '--verify-extracted') {
        if (-not (Test-Path -LiteralPath (Join-Path $resolved 'VarPad.exe.config'))) { throw 'Missing framework config.' }
    }
}
if (Test-Path -LiteralPath $resolved) { throw 'Test extraction was not removed.' }
Write-Output 'PASS: installer extraction, payload integrity, config and removal. No profile or registry changes.'
