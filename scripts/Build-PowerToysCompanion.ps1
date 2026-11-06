param([ValidateSet('x64', 'ARM64')][string]$Architecture = 'x64')
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
[xml]$taskProperties = Get-Content -LiteralPath (Join-Path $taskRoot 'Directory.Build.props') -Raw
$taskVersion = [string]$taskProperties.Project.PropertyGroup.Version
if ($taskVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected a stable source version for the companion.' }
$taskRuntime = 'win-' + $Architecture.ToLowerInvariant()
$taskOutput = Join-Path $taskRoot ('artifacts\powertoys\' + $taskRuntime)
$taskStaging = Join-Path $taskOutput 'package'
$taskProject = Join-Path $taskRoot 'src\ExactFrame.CommandPalette\ExactFrame.CommandPalette.csproj'
New-Item -ItemType Directory -Path $taskStaging -Force | Out-Null
if (Get-ChildItem -LiteralPath $taskStaging -Force | Select-Object -First 1) {
    $taskResolvedStaging = [IO.Path]::GetFullPath($taskStaging)
    $taskAllowedRoot = [IO.Path]::GetFullPath((Join-Path $taskRoot 'artifacts\powertoys')) + [IO.Path]::DirectorySeparatorChar
    if (-not $taskResolvedStaging.StartsWith($taskAllowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Package staging must stay inside artifacts/powertoys.' }
    Remove-Item -LiteralPath $taskResolvedStaging -Recurse -Force
    New-Item -ItemType Directory -Path $taskStaging -Force | Out-Null
}

& dotnet publish $taskProject -c Release "-p:Platform=$Architecture" -r $taskRuntime --self-contained true '-p:PublishSingleFile=false' '-p:RestoreLockedMode=true' -o $taskStaging
if ($LASTEXITCODE -ne 0) { throw 'The Command Palette companion did not publish.' }

[xml]$taskManifest = Get-Content -LiteralPath (Join-Path $taskStaging 'Package.appxmanifest') -Raw
$taskManifest.Package.Identity.ProcessorArchitecture = $Architecture.ToLowerInvariant()
$taskManifest.Package.Identity.Version = $taskVersion + '.0'
$taskManifest.Save((Join-Path $taskStaging 'AppxManifest.xml'))
@"
ExactFrame $taskVersion - optional Command Palette companion ($taskRuntime)

This separate development package is optional. ExactFrame's portable app works without it or PowerToys.
Requires Windows 11, PowerToys Command Palette, and an existing Windows development-package setup.
It is unsigned and is not a normal double-click installer.

1. Extract this ZIP to a stable folder that you will keep.
2. Run Install-Companion.ps1 in PowerShell. It checks prerequisites and registers AppxManifest.xml.
   It never enables Developer Mode, changes certificate trust, or installs PowerToys.
3. Open ExactFrame $taskVersion or newer, then Profiles > PowerToys (optional) > Share saved profiles.
4. Reload Command Palette and search for ExactFrame. Use Refresh profiles after changing profiles.

To uninstall, remove ExactFrame for PowerToys from Windows Installed apps. Turn off sharing in ExactFrame if desired.
Production MSIX installation requires trusted signing or Store distribution; the unsigned MSIX is for development.
Setup: https://github.com/MaxAkbar/ExactFrame/blob/v$taskVersion/docs/powertoys-integration.md
"@ | Set-Content -LiteralPath (Join-Path $taskStaging 'README.txt') -Encoding utf8
$taskMakeAppx = Get-ChildItem -Path "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" |
    Sort-Object { [version]$_.Directory.Parent.Name } -Descending | Select-Object -First 1
if (-not $taskMakeAppx) { throw 'Install the Windows SDK packaging tools to build the optional MSIX.' }
$taskStem = "ExactFrame.CommandPalette-$taskVersion-$taskRuntime"
$taskPackage = Join-Path $taskOutput "$taskStem-unsigned.msix"
$taskPackOutput = & $taskMakeAppx.FullName pack /d $taskStaging /p $taskPackage /o 2>&1
$taskPackExit = $LASTEXITCODE
$taskPackOutput | Set-Content -LiteralPath (Join-Path $taskOutput 'makeappx.log')
if ($taskPackExit -ne 0) { throw "The Command Palette package failed validation. See $taskOutput\makeappx.log" }
$taskArchive = Join-Path $taskOutput "$taskStem-development.zip"
Compress-Archive -Path (Join-Path $taskStaging '*') -DestinationPath $taskArchive -CompressionLevel Optimal -Force
$taskChecksums = foreach ($taskFile in @($taskPackage, $taskArchive)) {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $taskFile -Algorithm SHA256).Hash.ToLowerInvariant(), (Split-Path $taskFile -Leaf)
}
$taskChecksums | Set-Content -LiteralPath (Join-Path $taskOutput 'POWERTOYS_SHA256SUMS.txt') -Encoding ascii
Write-Output "Built unsigned companion: $taskPackage"
Write-Output "Built development ZIP: $taskArchive"
Write-Output 'This script does not install, register, sign, or publish the companion.'
