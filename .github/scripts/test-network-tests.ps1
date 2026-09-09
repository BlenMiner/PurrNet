#requires -Version 7.0
# Real child processes exercise orchestration without launching Unity.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'This self-test targets the Windows runner.' }
$temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
$testDirectory = Join-Path $temporaryParent ('purrnet-runner-tests-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $testDirectory
$shellPath = (Get-Process -Id $PID).Path
$runner = Join-Path $PSScriptRoot 'network-tests.ps1'
$mockPath = Join-Path $testDirectory 'mock-player.ps1'
$configuration = Join-Path $testDirectory 'scenarios.json'
$fullConfiguration = Join-Path $testDirectory 'full-scenarios.json'
$previousFault = $env:PURR_RUNNER_TEST_FAULT
$checks = 0
try {
    @'
$ErrorActionPreference = 'Stop'
$values = @{}
for ($i = 0; $i -lt $args.Count; $i++) {
    if ($args[$i] -in @('-role','-count','-port','-migrationPort','-results','-logFile','-scenario','-serverHost')) {
        $key = $args[$i]
        $i++
        $values[$key] = $args[$i]
    }
}
$role = $values['-role']
$scenario = $values['-scenario']
$directory = Split-Path -Parent $values['-results']
$fault = $env:PURR_RUNNER_TEST_FAULT
Set-Content -LiteralPath $values['-logFile'] -Value 'Mock player started.'
if ($values['-port'] -eq $values['-migrationPort']) { exit 12 }
$entries = @(
    @{name='Bootstrap'; result=@{success=$true}},
    @{name=$scenario; result=@{success=$true}}
)
if (-not $scenario) {
    $entries = @('Bootstrap','MockFirst','MockSecond','MockLast') | ForEach-Object { @{name=$_; result=@{success=$true}} }
    # Every peer returns the same incorrect list, so comparing peers cannot catch it.
    if ($fault -eq 'full-wrong-name') { $entries[1].name = 'WrongScenario' }
    if ($fault -eq 'full-reordered') {
        $swap = $entries[1]; $entries[1] = $entries[2]; $entries[2] = $swap
    }
}
if ($scenario -eq 'UnexpectedHostLossMigrationScenario') {
    if ($role -ne 'client') {
        if ($fault -eq 'premature-exit') { exit 0 }
        if ($fault -eq 'authority-results') {
            ConvertTo-Json -InputObject $entries -Depth 6 | Set-Content -LiteralPath $values['-results']
        }
        if ($fault -ne 'missing-marker') {
            $ready = @{scenario=$scenario; ready=$true}
            if ($fault -eq 'invalid-marker') { $ready.scenario = 'WrongScenario' }
            $ready | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory 'ready.tmp')
            Move-Item -LiteralPath (Join-Path $directory 'ready.tmp') -Destination (Join-Path $directory 'migration-crash-ready.json')
        }
        if ($fault -eq 'post-ready-exit') { exit 17 }
        while ($true) { Start-Sleep -Milliseconds 100 }
    }
    $receiptPath = Join-Path $directory 'migration-crash-injected.json'
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    while (-not (Test-Path -LiteralPath $receiptPath)) {
        if ([DateTime]::UtcNow -gt $deadline) { exit 11 }
        Start-Sleep -Milliseconds 50
    }
    $receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
    if ($receipt.scenario -cne $scenario -or -not $receipt.injected -or $receipt.authorityPid -le 0) { exit 13 }
}
if ($role -eq 'client' -and (Split-Path -Leaf $values['-results']) -eq 'client-1.json') {
    switch ($fault) {
        'missing-result' { exit 0 }
        'failed-result' { $entries[1].result.success = $false }
        'reordered' { [array]::Reverse($entries) }
        'duplicate' { $entries += $entries[1] }
        'missing-script' { Add-Content -LiteralPath $values['-logFile'] -Value "The referenced script on this Behaviour (Game Object 'Probe') is missing!" }
        'missing-log' { Remove-Item -LiteralPath $values['-logFile'] }
        'other-exception' { Add-Content -LiteralPath $values['-logFile'] -Value 'System.IO.IOException: unexpected failure' }
        'assertion' { Add-Content -LiteralPath $values['-logFile'] -Value 'Assertion failed: invalid state' }
        'write-failure' { Add-Content -LiteralPath $values['-logFile'] -Value 'Failed to write results' }
    }
}
ConvertTo-Json -InputObject $entries -Depth 6 | Set-Content -LiteralPath $values['-results']
exit 0
'@ | Set-Content -LiteralPath $mockPath -Encoding utf8
    [ordered]@{ runs = @(
        @{name='normal'; scenario='MockMigrationScenario'; minimumPlayers=2; timeoutSeconds=20},
        @{name='crash'; scenario='UnexpectedHostLossMigrationScenario'; minimumPlayers=3; timeoutSeconds=20; fault='kill-authority'}
    ) } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $configuration -Encoding utf8
    @{ runs = @(@{ name='full'; scenario=''; minimumPlayers=2; timeoutSeconds=20;
        expectedScenarios=@('Bootstrap','MockFirst','MockSecond','MockLast') }) } |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $fullConfiguration -Encoding utf8

    function Run-Check([string] $Name, [string] $Selected, [string] $RunMode, [string] $Fault, [bool] $Expected, [int] $Seconds = 20, [string] $CaseConfiguration = $configuration) {
        $env:PURR_RUNNER_TEST_FAULT = $Fault
        $destination = Join-Path $testDirectory $Name
        $selection = @{}
        if ($Selected) { $selection.Scenario = $Selected }
        $output = & $runner -PlayerPath $shellPath -PlayerArguments @('-NoProfile','-File',$mockPath) `
            -OutputDirectory $destination -Mode $RunMode -TotalPlayers 3 @selection `
            -Configuration $CaseConfiguration -StartupDelayMilliseconds 0 -TimeoutSeconds $Seconds *>&1 | Out-String
        $code = $LASTEXITCODE
        if (($code -eq 0) -ne $Expected) { throw "Unexpected exit $code for ${Name}:`n$output" }
        $summary = Get-Content -LiteralPath (Join-Path $destination 'summary.json') -Raw | ConvertFrom-Json
        if ($summary.passed -ne $Expected) { throw "Incorrect summary outcome for $Name" }
        if ($Expected -and $Selected -eq 'UnexpectedHostLossMigrationScenario') {
            foreach ($batch in $summary.batches) {
                if (-not $batch.crashInjected -or @($batch.peers | Where-Object expectedCrash).Count -ne 1) {
                    throw 'Crash run did not record exactly one injected authority death.'
                }
            }
        }
        $script:checks++
        Write-Host "PASS $Name"
    }

    Run-Check normal-both MockMigrationScenario both '' $true
    Run-Check crash-both UnexpectedHostLossMigrationScenario both '' $true
    Run-Check missing-marker UnexpectedHostLossMigrationScenario host missing-marker $false 5
    Run-Check invalid-marker UnexpectedHostLossMigrationScenario host invalid-marker $false
    Run-Check premature-exit UnexpectedHostLossMigrationScenario host premature-exit $false
    Run-Check completed-authority UnexpectedHostLossMigrationScenario host authority-results $false
    Run-Check missing-survivor UnexpectedHostLossMigrationScenario host missing-result $false
    Run-Check failed-survivor UnexpectedHostLossMigrationScenario host failed-result $false
    Run-Check reordered-survivor UnexpectedHostLossMigrationScenario host reordered $false
    Run-Check duplicate-survivor UnexpectedHostLossMigrationScenario host duplicate $false
    Run-Check missing-script MockMigrationScenario host missing-script $false
    Run-Check missing-log MockMigrationScenario host missing-log $false
    Run-Check other-exception MockMigrationScenario host other-exception $false
    Run-Check assertion MockMigrationScenario host assertion $false
    Run-Check write-failure MockMigrationScenario host write-failure $false
    Run-Check full-both '' both '' $true -CaseConfiguration $fullConfiguration
    Run-Check full-wrong-name '' host full-wrong-name $false -CaseConfiguration $fullConfiguration
    Run-Check full-reordered '' host full-reordered $false -CaseConfiguration $fullConfiguration

    $missingExpectedConfiguration = Join-Path $testDirectory 'missing-expected.json'
    @{ runs = @(@{ name='full'; scenario=''; minimumPlayers=2; timeoutSeconds=20 }) } |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $missingExpectedConfiguration -Encoding utf8
    $rejected = $false
    $missingExpectedOutput = Join-Path $testDirectory 'missing-expected-list'
    try {
        & $runner -PlayerPath $shellPath -OutputDirectory $missingExpectedOutput -Mode host `
            -Configuration $missingExpectedConfiguration
    }
    catch { $rejected = $_.Exception.Message -eq 'Each full-suite batch must configure its exact expected scenario list.' }
    if (-not $rejected -or (Test-Path -LiteralPath $missingExpectedOutput)) {
        throw 'Missing expected scenario list was not rejected before launch.'
    }
    $checks++
    Write-Host 'PASS missing-expected-list'

    # Deterministically model death between the runner's last HasExited check
    # and Kill. Calling the production helper on a real exited process must
    # reject its nonzero exit even though Process.Kill itself returns normally.
    $parserErrors = $null
    $runnerAst = [Management.Automation.Language.Parser]::ParseFile($runner, [ref]$null, [ref]$parserErrors)
    if ($parserErrors.Count) { throw 'Runner parser errors.' }
    $crashHelper = $runnerAst.Find({
        param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq 'Stop-AuthorityForCrash'
    }, $false).Body.GetScriptBlock()
    $postReadyDirectory = Join-Path $testDirectory 'post-ready-accidental-exit'
    $null = New-Item -ItemType Directory -Path $postReadyDirectory
    $env:PURR_RUNNER_TEST_FAULT = 'post-ready-exit'
    $escapedMock = $mockPath.Replace("'", "''")
    $escapedDirectory = $postReadyDirectory.Replace("'", "''")
    $mockCommand = "& '$escapedMock' -role host -scenario UnexpectedHostLossMigrationScenario -port 51001 -migrationPort 51002 -results '$escapedDirectory/host.json' -logFile '$escapedDirectory/host.log'" + '; exit $LASTEXITCODE'
    $encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($mockCommand))
    $postReadyProcess = Start-Process -FilePath $shellPath -ArgumentList @('-NoProfile', '-EncodedCommand', $encodedCommand) -WindowStyle Hidden -PassThru
    $null = $postReadyProcess.Handle
    try {
        if (-not $postReadyProcess.WaitForExit(5000)) { throw 'Post-ready mock did not exit.' }
        if ($postReadyProcess.ExitCode -ne 17 -or
            -not (Test-Path -LiteralPath (Join-Path $postReadyDirectory 'migration-crash-ready.json'))) {
            throw 'Post-ready mock did not establish the intended accidental death.'
        }
        $rejectedCrash = $false
        try { & $crashHelper $postReadyProcess }
        catch { $rejectedCrash = $_.Exception.Message -eq 'Could not verify that the requested authority crash occurred.' }
        if (-not $rejectedCrash) { throw 'An accidental post-ready authority exit was accepted as an injected crash.' }
        $checks++
        Write-Host 'PASS post-ready-accidental-exit'
    }
    finally {
        if (-not $postReadyProcess.HasExited) { $postReadyProcess.Kill(); $postReadyProcess.WaitForExit() }
        $postReadyProcess.Dispose()
    }

    # Refuse stale directories before launching any process.
    $rejected = $false
    try {
        & $runner -PlayerPath $shellPath -OutputDirectory (Join-Path $testDirectory 'normal-both') `
            -Scenario MockMigrationScenario -Configuration $configuration
    }
    catch { $rejected = $_.Exception.Message -like 'OutputDirectory must be fresh:*' }
    if (-not $rejected) { throw 'Stale result directory was accepted.' }
    $checks++
    Write-Host 'PASS stale-directory'
    Write-Host "All $checks Windows runner checks passed."
}
finally {
    $env:PURR_RUNNER_TEST_FAULT = $previousFault
    $resolved = [IO.Path]::GetFullPath($testDirectory)
    if ($resolved.StartsWith($temporaryParent + '\purrnet-runner-tests-', [StringComparison]::OrdinalIgnoreCase) -and
        (Get-Item -LiteralPath $resolved).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw 'Refusing redirected temporary test directory.'
    }
    if (-not $resolved.StartsWith($temporaryParent + '\purrnet-runner-tests-', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Unexpected test cleanup boundary.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
