param([int]$Port = 4477, [string]$OutputDirectory = 'artifacts/recording-browser')
$ErrorActionPreference = 'Stop'
$directory = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($directory) | Out-Null
$fixture = [IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '../tests/ExactFrame.WindowsChecks/fixture/index.html'))
$listener = [Net.HttpListener]::new()
$listener.Prefixes.Add("http://127.0.0.1:$Port/")
$listener.Start()
Write-Output "Fixture listening at http://127.0.0.1:$Port/"
try {
    while ($listener.IsListening) {
        $context = $listener.GetContext()
        try {
            if ($context.Request.HttpMethod -eq 'POST' -and $context.Request.Url.AbsolutePath -eq '/metrics') {
                $reader = [IO.StreamReader]::new($context.Request.InputStream)
                try { $metrics = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
                # Write atomically so the check process never reads half of a JSON document.
                $json = $metrics | ConvertTo-Json -Depth 4
                [IO.File]::WriteAllText((Join-Path $directory 'metrics.tmp'), $json)
                [IO.File]::Move((Join-Path $directory 'metrics.tmp'), (Join-Path $directory 'metrics.json'), $true)
                $context.Response.StatusCode = 204
            } elseif ($context.Request.HttpMethod -eq 'GET' -and $context.Request.Url.AbsolutePath -eq '/') {
                $context.Response.ContentType = 'text/html; charset=utf-8'
                $context.Response.OutputStream.Write($fixture,0,$fixture.Length)
            } else { $context.Response.StatusCode = 404 }
        } finally { $context.Response.Close() }
    }
} finally { $listener.Stop(); $listener.Close() }
