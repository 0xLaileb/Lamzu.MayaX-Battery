# Refresh the tracked English screenshots using the application's own preview modes.
[CmdletBinding()]
param([switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $NoBuild) {
    dotnet build (Join-Path $root 'src/MayaX-Battery.csproj') -c Release
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }
}
$exe = Join-Path $root 'src/bin/Release/net10.0-windows/MayaX-Battery.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Build the Release configuration first.' }
$work = Join-Path $root ('artifacts/previews-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $work
$previews = @(
    @{ Mode = '--preview'; File = 'preview.png'; Options = @() },
    @{ Mode = '--ui-preview'; File = 'window-preview.png'; Options = @('normal', '1') },
    @{ Mode = '--menu-preview'; File = 'menu-preview.png'; Options = @() }
)
foreach ($preview in $previews) {
    $output = Join-Path $work $preview.File
    $arguments = @($preview.Mode, ('"' + $output + '"')) + $preview.Options + @('--language', 'en')
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(30000)) {
        $process.Kill()
        throw "Preview timed out: $($preview.File)"
    }
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $output)) {
        throw "Preview failed: $($preview.File)"
    }
    Copy-Item -LiteralPath $output -Destination (Join-Path $root ('docs/' + $preview.File))
    Write-Host "Updated docs/$($preview.File)"
}