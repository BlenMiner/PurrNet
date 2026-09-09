#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $PlayerPath,
    [Parameter(Mandatory)] [string] $OutputDirectory,
    [ValidateSet('server', 'host', 'both')] [string] $Mode = 'both',
    [ValidateRange(2, 32)] [int] $TotalPlayers = 5,
    [ValidatePattern('^[A-Za-z_][A-Za-z0-9_]*$')] [string] $Scenario,
    [string] $Configuration = (Join-Path $PSScriptRoot '../network-scenarios.json'),
    [ValidateRange(0, 7200)] [int] $TimeoutSeconds = 0,
    [ValidateRange(0, 10000)] [int] $StartupDelayMilliseconds = 1000,
    # Prefix arguments also allow testing the runner with a PowerShell mock player.
    [string[]] $PlayerArguments = @()
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Use network-tests.sh for Linux players.' }

function Quote-NativeArgument([AllowEmptyString()][string] $Value) {
    $escaped = [regex]::Replace($Value, '(\\*)"', '$1$1\"')
    $escaped = [regex]::Replace($escaped, '(\\+)$', '$1$1')
    return '"' + $escaped + '"'
}

function Write-AtomicJson($Value, [string] $Path) {
    $temporary = $Path + '.tmp'
    $Value | ConvertTo-Json -Depth 40 | Set-Content -LiteralPath $temporary -Encoding utf8
    [IO.File]::Move($temporary, $Path, $true)
}

function Stop-AuthorityForCrash {
    param([Diagnostics.Process] $Process)
    $Process.Kill()
    # Kill can return normally if the process exited just before the call.
    # Windows Process.Kill uses -1, so another exit is not an injected crash.
    if (-not $Process.WaitForExit(5000) -or $Process.ExitCode -ne -1) {
        throw 'Could not verify that the requested authority crash occurred.'
    }
}

function Get-TestPort {
    for ($attempt = 0; $attempt -lt 100; $attempt++) {
        $probe = [Net.Sockets.UdpClient]::new([Net.Sockets.AddressFamily]::InterNetwork)
        try {
            $probe.ExclusiveAddressUse = $true
            $probe.Client.Bind([Net.IPEndPoint]::new([Net.IPAddress]::Loopback, 0))
            $port = $probe.Client.LocalEndPoint.Port
            if ($usedPorts.Add($port)) { return $port }
        }
        finally { $probe.Dispose() }
    }
    throw 'Could not choose a distinct UDP port.'
}

function Start-Peer([string] $Role, [string] $Label) {
    $arguments = @($PlayerArguments) + @(
        '-batchmode', '-nographics', '-role', $Role, '-count', [string]$TotalPlayers,
        '-port', [string]$initialPort, '-migrationPort', [string]$migrationPort,
        '-serverHost', '127.0.0.1',
        '-results', (Join-Path $batchDirectory ($Label + '.json')),
        '-logFile', (Join-Path $batchDirectory ($Label + '.log'))
    )
    if ($spec.scenario) { $arguments += @('-scenario', $spec.scenario) }
    $process = Start-Process -FilePath $player -WorkingDirectory (Split-Path -Parent $player) `
        -ArgumentList (($arguments | ForEach-Object { Quote-NativeArgument $_ }) -join ' ') `
        -WindowStyle Hidden -PassThru
    $null = $process.Handle
    $peer = [pscustomobject]@{
        process = $process
        detail = [ordered]@{
            label = $Label; role = $Role; processId = $process.Id
            startedUtc = $process.StartTime.ToUniversalTime().ToString('o')
            exitCode = $null; cleanupRequested = $false; expectedCrash = $false
            resultsPath = Join-Path $batchDirectory ($Label + '.json')
            logPath = Join-Path $batchDirectory ($Label + '.log')
            results = @(); errors = @(); passed = $false
        }
    }
    $ownedPeers.Add($peer)
    return $peer
}

function Validate-Peer($Peer) {
    $detail = $Peer.detail
    $errors = [Collections.Generic.List[string]]::new()
    if ($detail.cleanupRequested) { $errors.Add('Player required forced cleanup.') }
    if ($detail.expectedCrash) {
        if (-not $injected -or $detail.exitCode -ne -1) {
            $errors.Add('The authority did not exit from the runner-injected crash.')
        }
        if (Test-Path -LiteralPath $detail.resultsPath) {
            $errors.Add('The crashed authority unexpectedly completed its result file.')
        }
    }
    else {
        if ($detail.exitCode -ne 0) { $errors.Add("Player exited $($detail.exitCode).") }
        try {
            $entries = ConvertFrom-Json -InputObject (Get-Content -LiteralPath $detail.resultsPath -Raw) -NoEnumerate
            if ($entries -isnot [array]) { throw 'Results must be a JSON array.' }
            if ($entries.Count -ne $spec.expectedScenarios.Count) { throw 'Incomplete or extra scenario results.' }
            for ($index = 0; $index -lt $entries.Count; $index++) {
                $entry = $entries[$index]
                if ($entry.name -isnot [string] -or $entry.name -cne $spec.expectedScenarios[$index]) {
                    throw 'Results do not match the exact configured scenario order.'
                }
                if ($entry.result.success -isnot [bool] -or -not $entry.result.success) {
                    throw "Failed scenario: $($entry.name)"
                }
            }
            $detail.results = $entries
        }
        catch { $errors.Add('Cannot validate results: ' + $_.Exception.Message) }
    }
    try {
        if (-not (Test-Path -LiteralPath $detail.logPath -PathType Leaf)) { throw 'Missing player log.' }
        # Expected failure scenarios capture only their exact anticipated exception;
        # every unexpected exception is still forwarded to the normal Unity logger.
        $pattern = '(?i)\b(?:[A-Za-z_][A-Za-z0-9_.]*)?Exception\b|Assertion failed|\bFATAL\b|CompleteSpawn: CreatePrototype failed|SceneID ''[0-9]+'' not found|Address already in use|The referenced script on this Behaviour .* is missing!|Segmentation fault|SIGSEGV|Crash!!!|Native Crash Reporting|Failed to write results'
        foreach ($diagnostic in Select-String -LiteralPath $detail.logPath -Pattern $pattern) {
            $errors.Add("Log line $($diagnostic.LineNumber): $($diagnostic.Line)")
        }
    }
    catch { $errors.Add('Cannot validate log: ' + $_.Exception.Message) }
    $detail.errors = $errors.ToArray()
    $detail.passed = $errors.Count -eq 0
}

$player = (Resolve-Path -LiteralPath $PlayerPath).ProviderPath
if (-not (Test-Path -LiteralPath $player -PathType Leaf)) { throw 'PlayerPath must be an executable file.' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw "OutputDirectory must be fresh: $output" }
$configurationData = Get-Content -LiteralPath $Configuration -Raw | ConvertFrom-Json
$runs = @($configurationData.runs)
if ($Scenario) {
    $runs = @($runs | Where-Object { $_.scenario -ceq $Scenario })
    if ($runs.Count -eq 0) {
        $runs = @([pscustomobject]@{
            name = $Scenario; scenario = $Scenario; minimumPlayers = 2; timeoutSeconds = 300
        })
    }
}
if ($runs.Count -eq 0) { throw 'Configuration contains no scenario batches.' }
$batchNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($spec in $runs) {
    if ($spec.name -notmatch '^[A-Za-z0-9_-]+$' -or -not $batchNames.Add($spec.name) -or
        $spec.timeoutSeconds -le 0 -or $spec.minimumPlayers -gt $TotalPlayers) {
        throw 'Invalid configuration or insufficient players.'
    }
    if ($spec.scenario) {
        $spec | Add-Member -NotePropertyName expectedScenarios -NotePropertyValue @('Bootstrap', $spec.scenario) -Force
    }
    if ($null -eq $spec.PSObject.Properties['expectedScenarios'] -or
        $spec.expectedScenarios -isnot [array] -or $spec.expectedScenarios.Count -eq 0) {
        throw 'Each full-suite batch must configure its exact expected scenario list.'
    }
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($name in $spec.expectedScenarios) {
        if ($name -isnot [string] -or [string]::IsNullOrWhiteSpace($name) -or -not $names.Add($name)) {
            throw 'Expected scenario names must be nonempty and unique.'
        }
    }
    if ($null -ne $spec.PSObject.Properties['fault'] -and
        ($spec.fault -cne 'kill-authority' -or $spec.scenario -cne 'UnexpectedHostLossMigrationScenario')) {
        throw 'Only UnexpectedHostLossMigrationScenario may request an authority crash.'
    }
}
$modes = if ($Mode -eq 'both') { @('server', 'host') } else { @($Mode) }
$usedPorts = [Collections.Generic.HashSet[int]]::new()
$batches = [Collections.Generic.List[object]]::new()
$null = New-Item -ItemType Directory -Path $output

foreach ($runMode in $modes) {
    $modeDirectory = Join-Path $output $runMode
    $null = New-Item -ItemType Directory -Path $modeDirectory
    foreach ($spec in $runs) {
        $batchDirectory = Join-Path $modeDirectory $spec.name
        $null = New-Item -ItemType Directory -Path $batchDirectory
        $initialPort = Get-TestPort
        $migrationPort = Get-TestPort
        $externalClients = if ($runMode -eq 'host') { $TotalPlayers - 1 } else { $TotalPlayers }
        $seconds = if ($TimeoutSeconds -gt 0) { $TimeoutSeconds } else { $spec.timeoutSeconds }
        $crashRequested = $null -ne $spec.PSObject.Properties['fault']
        $injected = $false
        $ownedPeers = [Collections.Generic.List[object]]::new()
        $batchErrors = [Collections.Generic.List[string]]::new()
        $timer = [Diagnostics.Stopwatch]::StartNew()
        Write-Host "Running $runMode/$($spec.name), $externalClients external clients, ports $initialPort -> $migrationPort"
        try {
            $authority = Start-Peer -Role $runMode -Label $runMode
            if ($StartupDelayMilliseconds) { Start-Sleep -Milliseconds $StartupDelayMilliseconds }
            if ($authority.process.HasExited) { throw 'Authority exited before clients launched.' }
            for ($index = 1; $index -le $externalClients; $index++) {
                $null = Start-Peer -Role client -Label "client-$index"
            }
            $nextProgress = 15
            while ($true) {
                if ($crashRequested -and -not $injected) {
                    if ($authority.process.HasExited) { throw 'Authority exited before the requested crash was injected.' }
                    $readyPath = Join-Path $batchDirectory 'migration-crash-ready.json'
                    if (Test-Path -LiteralPath $readyPath -PathType Leaf) {
                        $ready = Get-Content -LiteralPath $readyPath -Raw | ConvertFrom-Json
                        if ($ready.scenario -cne $spec.scenario -or $ready.ready -isnot [bool] -or -not $ready.ready) {
                            throw 'Invalid authority crash readiness marker.'
                        }
                        if ($authority.process.HasExited) { throw 'Authority exited before injection.' }
                        # Kill the retained Process object created by this runner; never
                        # discover a victim from file contents, process names, or ports.
                        Stop-AuthorityForCrash $authority.process
                        $injected = $true
                        $authority.detail.expectedCrash = $true
                        Write-AtomicJson ([ordered]@{
                            scenario = $spec.scenario; injected = $true; authorityPid = $authority.process.Id
                        }) (Join-Path $batchDirectory 'migration-crash-injected.json')
                        Write-Host "Injected authority crash for owned PID $($authority.process.Id)."
                    }
                }
                $active = @($ownedPeers | Where-Object { -not $_.process.HasExited })
                if ($active.Count -eq 0) { break }
                if ($timer.Elapsed.TotalSeconds -ge $seconds) { throw "Batch exceeded ${seconds}s timeout." }
                if ($timer.Elapsed.TotalSeconds -ge $nextProgress) {
                    Write-Host ("  {0:F0}s; active: {1}" -f $timer.Elapsed.TotalSeconds, ($active.detail.label -join ', '))
                    $nextProgress += 15
                }
                Start-Sleep -Milliseconds 100
            }
            if ($crashRequested -and -not $injected) { throw 'Required authority crash was never injected.' }
        }
        catch { $batchErrors.Add($_.Exception.Message) }
        finally {
            foreach ($peer in $ownedPeers) {
                try {
                    if (-not $peer.process.HasExited) {
                        $peer.detail.cleanupRequested = $true
                        $peer.process.Kill()
                        if (-not $peer.process.WaitForExit(5000)) { throw 'Owned player did not exit during cleanup.' }
                    }
                    $peer.process.WaitForExit()
                    $peer.detail.exitCode = $peer.process.ExitCode
                }
                catch { $batchErrors.Add("$($peer.detail.label) cleanup: $($_.Exception.Message)") }
                finally { $peer.process.Dispose() }
            }
            $timer.Stop()
        }
        foreach ($peer in $ownedPeers) { Validate-Peer $peer }
        $passed = $batchErrors.Count -eq 0 -and $ownedPeers.Count -eq ($externalClients + 1) -and
            @($ownedPeers | Where-Object { -not $_.detail.passed }).Count -eq 0
        $batch = [ordered]@{
            scenario = $spec.scenario; batch = $spec.name; mode = $runMode; passed = $passed
            crashInjected = $injected; initialPort = $initialPort; migrationPort = $migrationPort
            durationSeconds = [Math]::Round($timer.Elapsed.TotalSeconds, 3)
            errors = $batchErrors.ToArray(); peers = @($ownedPeers | ForEach-Object { $_.detail })
        }
        Write-AtomicJson $batch (Join-Path $batchDirectory 'run-summary.json')
        $batches.Add([pscustomobject]$batch)
        Write-AtomicJson ([ordered]@{ playerPath = $player; passed = @($batches | Where-Object { -not $_.passed }).Count -eq 0; batches = $batches.ToArray() }) (Join-Path $output 'summary.json')
        Write-Host ("{0}: {1}/{2}" -f $(if ($passed) { 'PASS' } else { 'FAIL' }), $runMode, $spec.name)
        if (-not $passed) { Write-Host ($batch | ConvertTo-Json -Depth 8) }
    }
}
if (@($batches | Where-Object { -not $_.passed }).Count) { exit 1 }
exit 0
