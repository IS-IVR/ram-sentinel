[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$AppVersion,

    [string]$PublishDirectory = 'artifacts\win-x64',
    [string]$OutputDirectory = 'artifacts\release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Resolve-RepositoryPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $Path))
}

$publishPath = Resolve-RepositoryPath -Path $PublishDirectory
$outputPath = Resolve-RepositoryPath -Path $OutputDirectory
$appExecutable = Join-Path $publishPath 'RamSentinel.exe'
$installerName = "RAM-Sentinel-Setup-win-x64.exe"
$portableName = "RAM-Sentinel-Portable-$AppVersion-win-x64.zip"
$installerPath = Join-Path $outputPath $installerName
$portablePath = Join-Path $outputPath $portableName
$checksumPath = Join-Path $outputPath 'SHA256SUMS.txt'

if (-not (Test-Path -LiteralPath $appExecutable -PathType Leaf)) {
    throw "Published application executable was not found: $appExecutable"
}

$compilerCommand = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
$compiler = $null
if ($compilerCommand) {
    $compiler = $compilerCommand.Source
}

if (-not $compiler) {
    $programFilesX86 = [Environment]::GetEnvironmentVariable('ProgramFiles(x86)')
    if ($programFilesX86) {
        $candidate = Join-Path $programFilesX86 'Inno Setup 6\ISCC.exe'
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $compiler = $candidate
        }
    }
}

if (-not $compiler) {
    throw 'Inno Setup 6 compiler (ISCC.exe) was not found. Install Inno Setup 6 before packaging.'
}

New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
foreach ($existingFile in @($installerPath, $portablePath, $checksumPath)) {
    if (Test-Path -LiteralPath $existingFile) {
        Remove-Item -LiteralPath $existingFile -Force
    }
}

$installerScript = Join-Path $repositoryRoot 'installer\RAMSentinel.iss'
$compilerArguments = @(
    "/DAppVersion=$AppVersion",
    "/DBuildDirectory=$publishPath",
    "/O$outputPath",
    $installerScript
)

Write-Host "Building installer for RAM Sentinel $AppVersion..."
& $compiler @compilerArguments
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    throw "Installer output was not created: $installerPath"
}

Write-Host 'Creating portable ZIP...'
Compress-Archive -LiteralPath $appExecutable -DestinationPath $portablePath -CompressionLevel Optimal

$releaseAssets = @($installerPath, $portablePath)
$checksumLines = foreach ($asset in $releaseAssets) {
    $hash = (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $(Split-Path -Path $asset -Leaf)"
}
[System.IO.File]::WriteAllLines(
    $checksumPath,
    $checksumLines,
    [System.Text.Encoding]::ASCII
)

foreach ($asset in @($releaseAssets + $checksumPath)) {
    $item = Get-Item -LiteralPath $asset
    Write-Host ("Created {0} ({1:N0} bytes)" -f $item.Name, $item.Length)
}

Write-Host "Release assets are ready in: $outputPath"
