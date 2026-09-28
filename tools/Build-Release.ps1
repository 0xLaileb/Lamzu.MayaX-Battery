# Publish one self-contained EXE and verify its native UI preview modes.
[CmdletBinding()]
param(
    [ValidatePattern('^v?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$')]
    [string]$Version = '0.0.0-dev'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$versionNumber = $Version -replace '^v', ''
$packageName = "MayaX-Battery-$versionNumber-win-x64"
$work = Join-Path $root ('artifacts/package-' + [guid]::NewGuid().ToString('N'))
$publish = Join-Path $work $packageName
$release = Join-Path $root 'artifacts/release'
$null = New-Item -ItemType Directory -Force -Path $publish, $release

dotnet publish (Join-Path $root 'src/MayaX-Battery.csproj') -c Release -r win-x64 --self-contained true -p:Version=$versionNumber -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o $publish
if ($LASTEXITCODE -ne 0) { throw "Publish failed: $LASTEXITCODE" }
$files = @(Get-ChildItem -LiteralPath $publish -File -Recurse)
if ($files.Count -ne 1 -or $files[0].Name -ne 'MayaX-Battery.exe') {
    throw 'Expected a single MayaX-Battery.exe, but publish produced additional files.'
}
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $publish

foreach ($mode in @('--preview', '--ui-preview', '--menu-preview')) {
    $preview = Join-Path $work ($mode.TrimStart('-') + '.png')
    $arguments = @($mode, ('"' + $preview + '"'), '--language', 'en')
    $process = Start-Process -FilePath (Join-Path $publish 'MayaX-Battery.exe') -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(30000)) {
        $process.Kill()
        throw "Published executable timed out in $mode."
    }
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $preview)) {
        throw "Published executable failed in $mode."
    }
    if ((Get-Item -LiteralPath $preview).Length -eq 0) { throw "Empty image from $mode." }
}

$zip = Join-Path $release "$packageName.zip"
Compress-Archive -LiteralPath $publish -DestinationPath $zip -Force
$checksum = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $release 'SHA256SUMS.txt'), "$checksum  $packageName.zip", [Text.UTF8Encoding]::new($false))
Write-Host "Published directory: $publish"
Write-Host "Package: $zip"
Write-Host "SHA256: $checksum"