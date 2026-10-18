[CmdletBinding()]
param(
    [ValidateRange(1, 10)] [int] $Repeat = 3,
    [string] $BrowserWindowTitle,
    [string] $OutputDirectory = ('artifacts/responsiveness/' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$destination = [IO.Path]::GetFullPath($OutputDirectory, $repoRoot)
if (Test-Path -LiteralPath $destination) { throw "Preserve the existing run and choose a new output directory: $destination" }
New-Item -ItemType Directory -Path $destination | Out-Null

Push-Location $repoRoot
try {
    & dotnet restore tests/ExactFrame.WindowsChecks/ExactFrame.WindowsChecks.csproj -p:Platform=x64 --locked-mode *> (Join-Path $destination 'restore.log')
    if ($LASTEXITCODE) { throw 'Restore failed; see restore.log.' }
    & dotnet build tests/ExactFrame.WindowsChecks/ExactFrame.WindowsChecks.csproj -c Release -p:Platform=x64 --no-restore *> (Join-Path $destination 'build.log')
    if ($LASTEXITCODE) { throw 'Build failed; see build.log.' }
    $exe = Join-Path $repoRoot 'tests/ExactFrame.WindowsChecks/bin/x64/Release/net10.0-windows10.0.19041.0/ExactFrame.WindowsChecks.exe'
    $sources = @(& rg --files src tests scripts .github -g '*.cs' -g '*.xaml' -g '*.csproj' -g '*.props' -g '*.json' -g '*.ps1' -g '*.yml')
    if ($LASTEXITCODE) { throw 'Source inventory failed.' }
    $sources += 'global.json', 'Directory.Build.props', 'NuGet.config'
    $manifest = foreach ($path in ($sources | Sort-Object -Unique)) {
        [ordered]@{ path = $path; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
    [ordered]@{
        utc = [DateTime]::UtcNow.ToString('o'); sdk = (& dotnet --version)
        executableSha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
        sources = @($manifest)
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $destination 'inputs.json') -Encoding utf8

    $summary = @()
    for ($run = 1; $run -le $Repeat; $run++) {
        $runPath = Join-Path $destination "run-$run"
        $start = [Diagnostics.ProcessStartInfo]::new($exe)
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.WorkingDirectory = $repoRoot
        $start.ArgumentList.Add('--responsiveness-only')
        $start.ArgumentList.Add('--output')
        $start.ArgumentList.Add($runPath)
        if ($BrowserWindowTitle) {
            $start.ArgumentList.Add('--performance-browser-title')
            $start.ArgumentList.Add($BrowserWindowTitle)
        }
        Write-Output "Measuring repeat $run of $Repeat"
        $process = [Diagnostics.Process]::Start($start)
        try {
            if (!$process.WaitForExit(60000)) {
                $process.Kill()
                $process.WaitForExit()
                throw "Repeat $run timed out. Partial evidence is in $runPath."
            }
            if ($process.ExitCode) { throw "Repeat $run failed; see $runPath/windows-checks.json." }
        }
        finally { $process.Dispose() }
        $report = Get-Content -LiteralPath (Join-Path $runPath 'windows-checks.json') -Raw | ConvertFrom-Json
        if ($report.error) { throw $report.error }
        foreach ($check in $report.checks | Where-Object kind -eq 'responsiveness') {
            $row = [ordered]@{ repeat = $run; scenario = $check.scenario; status = $check.status; overBudget = $check.overBudget; observationKinds = $check.observationKinds }
            foreach ($metric in 'operationMs', 'dispatcherGapMs') {
                $values = @($check.$metric | Sort-Object)
                if ($values.Count) {
                    $row[$metric] = [ordered]@{ count = $values.Count; p50 = $values[[Math]::Ceiling($values.Count * 0.5) - 1]; p95 = $values[[Math]::Ceiling($values.Count * 0.95) - 1]; max = $values[-1] }
                }
            }
            $summary += [pscustomobject] $row
        }
        $summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $destination 'summary.json') -Encoding utf8
    }
    Write-Output "Raw evidence and per-repeat summary: $destination"
}
finally { Pop-Location }
