[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Version.Build -lt 22000) {
    throw 'The Command Palette companion requires Windows 11. ExactFrame works independently without it.'
}
if (-not (Get-AppxPackage 'Microsoft.CommandPalette')) {
    throw 'Install PowerToys with Command Palette before registering this optional companion.'
}
$taskDeveloperMode = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' `
    -Name AllowDevelopmentWithoutDevLicense -ErrorAction SilentlyContinue
if ($taskDeveloperMode.AllowDevelopmentWithoutDevLicense -ne 1) {
    throw 'This unsigned development package requires an existing Windows development-package setup. No Windows settings were changed. ExactFrame works without this companion.'
}
$taskManifestPath = Join-Path $PSScriptRoot 'AppxManifest.xml'
[xml]$taskManifest = Get-Content -LiteralPath $taskManifestPath -Raw
if ($taskManifest.Package.Identity.Name -ne 'MaxAkbar.ExactFrame.CommandPalette' -or
    $taskManifest.Package.Identity.Publisher -ne 'CN=MaxAkbar') {
    throw 'This folder does not contain the expected ExactFrame companion package.'
}
Add-AppxPackage -Register $taskManifestPath -ErrorAction Stop
Write-Output 'Registered ExactFrame for PowerToys. Reload Command Palette, then enable Share saved profiles in ExactFrame if desired.'
