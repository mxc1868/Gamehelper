# Exercise only build helper functions in a disposable workspace; never build,
# deploy to Test, launch a process, or touch real user settings.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$buildPath = Join-Path $projectRoot 'scripts\build.ps1'
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($buildPath, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw 'build.ps1 contains syntax errors' }
$helpers = @('Invoke-Robocopy', 'Save-DeployUserData', 'Restore-DeployUserData',
    'Get-BlockingGameHelperProcesses', 'Assert-DeployNotRunning', 'Backup-DeployUserData', 'Remove-DeployDirectory')
foreach ($node in $ast.FindAll({ param($item) $item -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true)) {
    if ($node.Name -in $helpers) { . ([ScriptBlock]::Create($node.Extent.Text)) }
}

$scratch = Join-Path $projectRoot ('artifacts\config-recovery\tests-' + [guid]::NewGuid().ToString('N'))
$scratch = [System.IO.Path]::GetFullPath($scratch)
$allowedRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts\config-recovery')) + '\'
if (-not $scratch.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid test workspace' }
$source = Join-Path $scratch 'runtime'
$backup = Join-Path $scratch 'backup'
$archives = Join-Path $scratch 'archives'
New-Item -ItemType Directory -Path $source -Force | Out-Null
$script:checks = 0
function Check([bool]$condition, [string]$description) {
    if (-not $condition) { throw "FAIL $description" }
    $script:checks++
    Write-Host "PASS $description"
}
function Put([string]$path, [string]$text) {
    New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force | Out-Null
    [System.IO.File]::WriteAllText($path, $text)
}
$script:fakeProcesses = @()
function Get-Process { param($ErrorAction) return $script:fakeProcesses }

try {
    $exe = Join-Path $source 'RandomOverlay.exe'
    $config = Join-Path $source 'configs\plugins.json'
    Put $exe 'dummy executable; never run'
    Put $config '{"Radar":{"Enable":true}}'
    $script:fakeProcesses = @([pscustomobject]@{ ProcessName='RandomOverlay'; Id=123; MainModule=[pscustomobject]@{FileName=$exe} })
    Check (@(Get-BlockingGameHelperProcesses $source).Count -eq 1) 'random launcher executable is detected'
    $blocked = $false
    try { Backup-DeployUserData $source $backup $archives } catch { $blocked = $true }
    Check ($blocked -and !(Test-Path $backup) -and (Test-Path $config)) 'running process blocks before backup writes or deployment deletion'

    $unreadable = [pscustomobject]@{ ProcessName='RandomOverlay'; Id=124 }
    $unreadable | Add-Member -MemberType ScriptProperty -Name MainModule -Value { throw 'Access denied' }
    $script:fakeProcesses = @($unreadable)
    Check (@(Get-BlockingGameHelperProcesses $source).Count -eq 1) 'unreadable matching process blocks conservatively'
    $script:fakeProcesses = @([pscustomobject]@{ ProcessName='RandomOverlay'; Id=125; MainModule=[pscustomobject]@{FileName=(Join-Path $scratch 'other\RandomOverlay.exe')} })
    Check (@(Get-BlockingGameHelperProcesses $source).Count -eq 0) 'readable process in another directory does not block this deployment'
    $script:fakeProcesses = @([pscustomobject]@{ ProcessName='Worker'; Id=126; MainModule=[pscustomobject]@{FileName=(Join-Path $source 'tools\Worker.exe')} })
    Check (@(Get-BlockingGameHelperProcesses $source).Count -eq 1) 'process inside deployment subdirectory is detected'
    $script:fakeProcesses = @()

    $lockedPath = Join-Path $source 'zz-locked.dll'
    Put $lockedPath 'locked file'
    $lock = [System.IO.File]::Open($lockedPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::None)
    try {
        $blocked = $false
        try { Remove-DeployDirectory $source } catch { $blocked = $true }
        Check ($blocked -and (Test-Path $config) -and (Test-Path $exe)) 'locked DLL is found before any configuration is deleted'
    }
    finally { $lock.Dispose() }

    Put (Join-Path $backup 'configs\core_settings.json') '{"sentinel":"original"}'
    Put (Join-Path $backup 'Plugins\Follower\config\settings.txt') '{"StopDistance":18}'
    Put (Join-Path $source 'Plugins\Follower\config\settings.txt') '{"StopDistance":22}'
    Backup-DeployUserData $source $backup $archives
    $firstArchive = @(Get-ChildItem -LiteralPath $archives -Directory)[0].FullName
    Check ((Get-Content -LiteralPath (Join-Path $firstArchive 'Plugins\Follower\config\settings.txt') -Raw) -eq '{"StopDistance":18}') 'prior plugin settings are archived before the working backup changes'
    Check ((Get-Content -LiteralPath (Join-Path $backup 'configs\core_settings.json') -Raw) -eq '{"sentinel":"original"}') 'missing source core config cannot erase the surviving backup'
    Check ((Get-Content -LiteralPath (Join-Path $backup 'Plugins\Follower\config\settings.txt') -Raw) -eq '{"StopDistance":22}') 'current plugin settings update the working backup'
    Backup-DeployUserData $source $backup $archives
    Check (@(Get-ChildItem -LiteralPath $archives -Directory).Count -eq 2) 'retries create separate archives instead of overwriting the recovery point'

    $restored = Join-Path $scratch 'restored'
    New-Item -ItemType Directory -Path $restored | Out-Null
    Check (Restore-DeployUserData $restored $backup) 'backup restores to a fresh runtime directory'
    Check ((Get-Content -LiteralPath (Join-Path $restored 'configs\core_settings.json') -Raw) -eq '{"sentinel":"original"}') 'restore recovers config missing from the damaged source'
    Remove-DeployDirectory $source
    Check (!(Test-Path $source) -and (Test-Path (Join-Path $backup 'configs\core_settings.json'))) 'unlocked cleanup leaves the backup intact'
    Write-Host "All $script:checks deployment checks passed. Real Test/configs were not touched."
}
finally {
    if ($scratch.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $scratch)) {
        Remove-Item -LiteralPath $scratch -Recurse -Force
    }
}
