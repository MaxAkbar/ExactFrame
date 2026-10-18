[CmdletBinding()]
param(
    [ValidateSet('x64', 'ARM64')]
    [string[]] $Architecture = @('x64', 'ARM64'),
    [switch] $LockedRestore,
    [string] $OutputRoot = 'artifacts/release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
[xml] $buildProperties = Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props') -Raw
$version = [string] $buildProperties.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Expected a stable version in Directory.Build.props; found '$version'."
}

$outputDirectory = Join-Path ([IO.Path]::GetFullPath($OutputRoot, $repoRoot)) $version
$buildDirectory = Join-Path $outputDirectory ('build-' + [guid]::NewGuid().ToString('N'))
$packages = @()

Push-Location $repoRoot
try {
    New-Item -ItemType Directory -Path $buildDirectory -Force | Out-Null
    foreach ($targetArchitecture in ($Architecture | Select-Object -Unique)) {
        $rid = 'win-' + $targetArchitecture.ToLowerInvariant()
        $assetName = "ExactFrame-$version-$rid-portable.zip"
        $archivePath = Join-Path $outputDirectory $assetName
        if (Test-Path -LiteralPath $archivePath) {
            throw "Package already exists: $archivePath. Preserve it or choose a new version before rebuilding."
        }

        $publishDirectory = Join-Path $buildDirectory $rid
        [string[]] $restoreArguments = @()
        if ($LockedRestore) { $restoreArguments += '-p:RestoreLockedMode=true' }
        & dotnet publish src/ExactFrame/ExactFrame.csproj -c Release -r $rid @restoreArguments `
            "-p:Platform=$targetArchitecture" --self-contained true `
            -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
            -p:DebugType=None -p:DebugSymbols=false -p:ContinuousIntegrationBuild=true `
            -o $publishDirectory
        if ($LASTEXITCODE -ne 0) {
            throw "Publishing $rid failed with exit code $LASTEXITCODE."
        }
        if (!(Test-Path -LiteralPath (Join-Path $publishDirectory 'ExactFrame.exe'))) {
            throw "Publishing $rid did not produce ExactFrame.exe."
        }

        @"
ExactFrame $version - $rid

GET STARTED
1. Extract this ZIP to a folder you want to keep.
2. Open ExactFrame.exe. No .NET or Windows App SDK installation is required.
3. Choose a size and select Show outline, or choose Resize window and pick an app.
4. Set the capture region in your recorder. ExactFrame prepares the area; it does not record video.

REQUIREMENTS
Windows 10 version 1809 or later; Windows 11 recommended.
Hide from recorders requires Windows 10 version 2004 or later and a compatible capture method.
Use win-x64 on Intel/AMD Windows PCs and win-arm64 on Windows on Arm.

BACKGROUND OPERATION
Closing the window leaves ExactFrame in the notification area by default.
To exit fully, right-click its notification-area icon and choose Quit ExactFrame.
If you enable Start with Windows, keep the application in a stable location.

SETTINGS
Settings are stored per user in %LOCALAPPDATA%\ExactFrame\exactframe.db.
The application is portable to run; its settings are not stored alongside the executable.

HELP AND SOURCE
Press F1 for help.
Documentation and source: https://github.com/MaxAkbar/ExactFrame/tree/v$version
Report an issue: https://github.com/MaxAkbar/ExactFrame/issues

These release builds are not code-signed. Windows may show an unknown-publisher warning.
The bundled IBM Plex font license is included in OFL.txt.
"@ | Set-Content -LiteralPath (Join-Path $publishDirectory 'README.txt') -Encoding utf8
        Copy-Item -LiteralPath (Join-Path $repoRoot 'src/ExactFrame/Assets/Fonts/OFL.txt') `
            -Destination (Join-Path $publishDirectory 'OFL.txt')

        Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $archivePath -CompressionLevel Optimal
        $packages += Get-Item -LiteralPath $archivePath
    }

    $checksumLines = foreach ($package in $packages) {
        '{0}  {1}' -f (Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $package.Name
    }
    $checksumPath = Join-Path $outputDirectory 'SHA256SUMS.txt'
    $checksumLines | Set-Content -LiteralPath $checksumPath -Encoding ascii
    $packages | Select-Object Name, Length, FullName
    Write-Output "Checksums: $checksumPath"
}
finally {
    Pop-Location
}
