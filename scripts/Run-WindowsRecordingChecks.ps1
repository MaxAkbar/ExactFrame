param(
    [string]$OutputDirectory = ('artifacts/recording-checks/' + (Get-Date -Format 'yyyyMMdd-HHmmss')),
    [string]$ObsConfigFile,
    [int]$ObsPort = 4456,
    [int]$BrowserProcessId = 0,
    [string]$BrowserMetricsFile
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$previousDirectory = Get-Location
try {
    Set-Location -LiteralPath $root
    $outputPath = [IO.Path]::GetFullPath($OutputDirectory)
    if (Test-Path -LiteralPath $outputPath) { throw 'Choose a new output directory; earlier attempts must be preserved.' }
    [IO.Directory]::CreateDirectory($outputPath) | Out-Null
    $sourceFiles = & git ls-files -co --exclude-standard -- src/ExactFrame src/ExactFrame.Core tests/ExactFrame.WindowsChecks tests/ExactFrame.Core.Tests/Fakes.cs scripts global.json Directory.Build.props
    $fingerprints = $sourceFiles | Sort-Object -Unique | ForEach-Object {
        if (Test-Path -LiteralPath $_ -PathType Leaf) {
            $snapshotPath = Join-Path $outputPath ('source-snapshot/' + $_)
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($snapshotPath)) | Out-Null
            Copy-Item -LiteralPath $_ -Destination $snapshotPath
            @{ path = $_; sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash }
        }
    }
    @{ commit = (& git rev-parse HEAD); status = @(& git status --short); sourceFiles = @($fingerprints) } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputPath 'source-manifest.json')
    & dotnet build tests/ExactFrame.WindowsChecks/ExactFrame.WindowsChecks.csproj -c Release -p:Platform=x64 2>&1 |
        Tee-Object -FilePath (Join-Path $outputPath 'build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Windows check harness build failed.' }
    $arguments = @('--output', ('"' + $outputPath + '"'))
    if ($ObsConfigFile) { $arguments += @('--obs-config', ('"' + [IO.Path]::GetFullPath($ObsConfigFile) + '"'), '--obs-port', $ObsPort) }
    if ($BrowserProcessId) { $arguments += @('--browser-pid', $BrowserProcessId) }
    if ($BrowserMetricsFile) { $arguments += @('--browser-metrics', ('"' + [IO.Path]::GetFullPath($BrowserMetricsFile) + '"')) }
    $executable = Join-Path $root 'tests/ExactFrame.WindowsChecks/bin/x64/Release/net10.0-windows10.0.19041.0/ExactFrame.WindowsChecks.exe'
    $binaryFiles = @('ExactFrame.WindowsChecks.exe','ExactFrame.WindowsChecks.dll','ExactFrame.Core.dll','resources.pri','ExactFrame.WindowsChecks.deps.json','ExactFrame.WindowsChecks.runtimeconfig.json')
    $binaryDirectory = [IO.Path]::GetDirectoryName($executable)
    [IO.Directory]::CreateDirectory((Join-Path $outputPath 'binaries')) | Out-Null
    $binaries = foreach ($binaryFile in $binaryFiles) {
        $binaryPath = Join-Path $binaryDirectory $binaryFile
        if (Test-Path -LiteralPath $binaryPath) {
            Copy-Item -LiteralPath $binaryPath -Destination (Join-Path $outputPath 'binaries')
            @{ file = $binaryFile; sha256 = (Get-FileHash -LiteralPath $binaryPath -Algorithm SHA256).Hash }
        }
    }
    $binaries | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputPath 'binary-manifest.json')
    $process = Start-Process -FilePath $executable -ArgumentList $arguments -WorkingDirectory $root -WindowStyle Hidden -PassThru
    function Stop-IsolatedRecording {
        if (-not $ObsConfigFile) { return }
        $cleanupArguments = @('--output', ('"' + $outputPath + '"'), '--stop-isolated-recording',
            '--obs-config', ('"' + [IO.Path]::GetFullPath($ObsConfigFile) + '"'), '--obs-port', $ObsPort)
        $cleanup = Start-Process -FilePath $executable -ArgumentList $cleanupArguments -WorkingDirectory $root -WindowStyle Hidden -PassThru
        if (-not $cleanup.WaitForExit(30000)) { Stop-Process -Id $cleanup.Id; throw 'Recorder cleanup timed out. Stop the isolated OBS recording manually.' }
        $cleanup.Refresh()
        if ($cleanup.ExitCode -ne 0) { throw 'Recorder cleanup failed. Stop the isolated OBS recording manually.' }
    }
    if (-not $process.WaitForExit(240000)) {
        try { Stop-IsolatedRecording } finally { Stop-Process -Id $process.Id }
        throw 'Windows checks exceeded four minutes; the owned test process was stopped. Preserve the output directory.'
    }
    $process.Refresh()
    Stop-IsolatedRecording
    if ($process.ExitCode -ne 0) { throw "Windows checks failed. Read $outputPath/windows-checks.json." }
    if (-not (Test-Path -LiteralPath (Join-Path $outputPath 'windows-checks.json'))) { throw 'No result manifest was written.' }
    if ($ObsConfigFile) {
        & python scripts/Analyze-RecordingChecks.py $outputPath 2>&1 | Tee-Object -FilePath (Join-Path $outputPath 'analysis.log')
        if ($LASTEXITCODE -ne 0) { throw 'Recorded-output checks failed. Preserve all clips and analysis.' }
    }
    Write-Output "Evidence saved in $outputPath"
} finally { Set-Location -LiteralPath $previousDirectory }
