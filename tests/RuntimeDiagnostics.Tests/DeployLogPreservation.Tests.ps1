$ErrorActionPreference = 'Stop'
$taskProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$taskFixtureRoot = Join-Path $taskProjectRoot ('artifacts/diagnostics/deploy-log-test-' + [guid]::NewGuid().ToString('N'))
$taskErrors = $null
$taskTokens = $null
$taskAst = [Management.Automation.Language.Parser]::ParseFile((Join-Path $taskProjectRoot 'scripts/build.ps1'), [ref]$taskTokens, [ref]$taskErrors)
if ($taskErrors.Count) { throw ($taskErrors | Out-String) }
foreach ($taskFunctionName in @('Invoke-Robocopy', 'Save-DeployUserData', 'Restore-DeployUserData')) {
    $taskFunction = $taskAst.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $taskFunctionName }, $true)
    . ([scriptblock]::Create($taskFunction.Extent.Text))
}
$taskSource = Join-Path $taskFixtureRoot 'source'
$taskBackup = Join-Path $taskFixtureRoot 'backup'
$taskTarget = Join-Path $taskFixtureRoot 'target'
$taskFiles = @('configs/core_settings.json', 'imgui.ini', 'Plugins/Example/config/settings.txt', 'diagnostics/runtime-current.log', 'diagnostics/runtime-previous-1.log', 'diagnostics/runtime-previous-2.log')
try {
    foreach ($taskRelative in $taskFiles) {
        $taskPath = Join-Path $taskSource $taskRelative
        New-Item -ItemType Directory -Path (Split-Path $taskPath -Parent) -Force | Out-Null
        [IO.File]::WriteAllBytes($taskPath, [Text.Encoding]::UTF8.GetBytes("fixture: $taskRelative`r`n"))
    }
    Save-DeployUserData -SourceDir $taskSource -BackupDir $taskBackup
    $taskRestored = Restore-DeployUserData -TargetDir $taskTarget -BackupDir $taskBackup
    if (-not $taskRestored) { throw 'Existing user settings were not reported as restored.' }
    foreach ($taskRelative in $taskFiles) {
        $taskOriginal = (Get-FileHash -LiteralPath (Join-Path $taskSource $taskRelative) -Algorithm SHA256).Hash
        foreach ($taskCopyRoot in @($taskBackup, $taskTarget)) {
            if ((Get-FileHash -LiteralPath (Join-Path $taskCopyRoot $taskRelative) -Algorithm SHA256).Hash -ne $taskOriginal) { throw "Changed bytes: $taskRelative" }
        }
        Write-Output "PASS deployment backup/restore preserves $taskRelative byte-for-byte"
    }
    $taskLogsOnly = Join-Path $taskFixtureRoot 'logs-only'
    New-Item -ItemType Directory -Path (Join-Path $taskLogsOnly 'diagnostics') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $taskLogsOnly 'diagnostics/runtime-current.log'), 'evidence')
    $taskLogsOnlyRestored = Restore-DeployUserData -TargetDir (Join-Path $taskFixtureRoot 'logs-target') -BackupDir $taskLogsOnly
    if ($taskLogsOnlyRestored) { throw 'Diagnostic logs must not suppress the existing missing-settings fallback.' }
    Write-Output 'PASS logs-only restore preserves the existing settings fallback'
} finally {
    $taskResolvedFixture = [IO.Path]::GetFullPath($taskFixtureRoot)
    $taskAllowedRoot = [IO.Path]::GetFullPath((Join-Path $taskProjectRoot 'artifacts/diagnostics')).TrimEnd('\') + '\'
    if (-not $taskResolvedFixture.StartsWith($taskAllowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing cleanup outside diagnostic fixtures.' }
    if (Test-Path -LiteralPath $taskResolvedFixture) { Remove-Item -LiteralPath $taskResolvedFixture -Recurse -Force }
}
