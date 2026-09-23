# Cache public PoE2DB item icons for offline rendering. Missing icons use text.
$ErrorActionPreference = 'Stop'
$plugin = Join-Path (Split-Path $PSScriptRoot -Parent) 'Plugins/UniqueLoot'
$output = Join-Path $plugin 'Icons'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$catalog = Get-Content (Join-Path $plugin 'Data/uniqueArtMapping.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$assets = @($catalog.PSObject.Properties.Name | Sort-Object)
$manifest = [ordered]@{}
$sha = [Security.Cryptography.SHA256]::Create()
$succeeded = 0
for ($offset = 0; $offset -lt $assets.Count; $offset += 4) {
    $jobs = foreach ($asset in $assets[$offset..([Math]::Min($offset + 3, $assets.Count - 1))]) {
        $key = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($asset.ToLowerInvariant()))).Replace('-', '').ToLowerInvariant()
        $file = "$key.webp"
        $target = Join-Path $output $file
        $url = 'https://cdn.poe2db.tw/image/' + $asset.Substring(0, $asset.Length - 4) + '.webp'
        $client = New-Object Net.WebClient
        $task = if (-not (Test-Path -LiteralPath $target)) { $client.DownloadFileTaskAsync($url, $target) } else { $null }
        [pscustomobject]@{ Asset = $asset; File = $file; Target = $target; Url = $url; Client = $client; Task = $task }
    }
    foreach ($job in $jobs) {
        try {
            if ($null -ne $job.Task -and -not $job.Task.Wait(20000)) { $job.Client.CancelAsync(); throw 'Download timeout' }
            $data = [IO.File]::ReadAllBytes($job.Target)
            if ($data.Length -lt 12 -or [Text.Encoding]::ASCII.GetString($data, 0, 4) -ne 'RIFF' -or [Text.Encoding]::ASCII.GetString($data, 8, 4) -ne 'WEBP') { throw 'Not a WebP image' }
            $manifest[$job.Asset] = [ordered]@{ File = $job.File; Url = $job.Url; Sha256 = (Get-FileHash -LiteralPath $job.Target -Algorithm SHA256).Hash.ToLowerInvariant(); Bytes = $data.Length }
            $succeeded++
        } catch {
            $manifest[$job.Asset] = [ordered]@{ Url = $job.Url; Error = $_.Exception.Message }
            # Remove only the exact output file from this attempted download.
            if (Test-Path -LiteralPath $job.Target) { Remove-Item -LiteralPath $job.Target }
        } finally { $job.Client.Dispose() }
    }
}
$sha.Dispose()
[IO.File]::WriteAllText((Join-Path $output 'sources.json'), ($manifest | ConvertTo-Json -Depth 4).Replace("`r`n", "`n") + "`n", [Text.UTF8Encoding]::new($false))
Write-Output "Icons: $succeeded / $($assets.Count); missing icons use text."
