param(
    [Parameter(Mandatory=$true)][string]$GameRoot,
    [Parameter(Mandatory=$true)][ValidatePattern('^[a-zA-Z0-9._-]+$')][string]$Label,
    [switch]$WithGraphics,
    [switch]$InteractiveSandbox,
    [switch]$StartupOnly,
    [ValidateRange(10,60)][int]$TimeoutSeconds=35
)
$ErrorActionPreference = 'Stop'
if (Get-Process -Name Tailside -ErrorAction SilentlyContinue) { throw 'Tailside is already running.' }
$GameRoot = (Resolve-Path -LiteralPath $GameRoot).Path
if (!(Test-Path -LiteralPath (Join-Path $GameRoot '.modsetup-sandbox')) -or
    $GameRoot.Contains('steamapps', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Marked isolated game copy required; regular game installations are not supported by this test.'
}
$probe = $null
$failure = $null
$cleanupErrors = [Collections.Generic.List[string]]::new()
$driver = $validation = $bepLog = $driverReport = $loaderReport = $null
try {
$driver = Join-Path $GameRoot 'BepInEx/plugins/BridgeTestDriver'
New-Item -ItemType Directory -Path $driver -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'TestDriver/bin/Release/netstandard2.1/BridgeTestDriver.dll') -Destination $driver
$validation = Join-Path $PSScriptRoot 'validation'
New-Item -ItemType Directory -Path $validation -Force | Out-Null
$playerLog = Join-Path $validation ($Label + '-player.log')
$bepLog = Join-Path $GameRoot 'BepInEx/LogOutput.log'
$driverReport = Join-Path $GameRoot 'BepInEx/config/BridgeTestDriver.report.log'
if (Test-Path -LiteralPath $driverReport) { Remove-Item -LiteralPath $driverReport -Force }
$loaderReport=$bepLog
# BepInEx owns its log; do not create or truncate a separate loader report.
foreach($suffix in @('bepinex','runtime','bridge','driver-diagnostic')){
    $previousOutput=Join-Path $validation ($Label+'-'+$suffix+'.log')
    if(Test-Path -LiteralPath $previousOutput){Remove-Item -LiteralPath $previousOutput -Force}
}
$oldTime = if (Test-Path -LiteralPath $bepLog) { (Get-Item -LiteralPath $bepLog).LastWriteTimeUtc } else { [datetime]::MinValue }
    $probeArgs=@('-batchmode','--bridge-smoke-test','-logFile',('"' + $playerLog + '"'))
    if ($InteractiveSandbox) {
        if (!(Test-Path -LiteralPath (Join-Path $GameRoot '.modsetup-sandbox'))) {throw 'Interactive tests require an isolated sandbox.'}
        $probeArgs=@('--bridge-smoke-test','-screen-fullscreen','0','-screen-width','960','-screen-height','540','-logFile',('"' + $playerLog + '"'))
    }
    if (!$WithGraphics) {$probeArgs=@('-nographics')+$probeArgs}
    if ($StartupOnly) {
        if (!(Test-Path -LiteralPath (Join-Path $GameRoot '.modsetup-sandbox'))) {throw 'Startup-only tests require an isolated sandbox.'}
        $probeArgs += '--loader-startup-test'
    }
    $probe = Start-Process -FilePath (Join-Path $GameRoot 'Tailside.exe') -WorkingDirectory $GameRoot -ArgumentList $probeArgs -WindowStyle Hidden -PassThru
    Write-Output ('Test PID=' + $probe.Id)
    if (!$probe.WaitForExit($TimeoutSeconds*1000)) {
        # This process was started here in batch mode without opening a save.
        Stop-Process -Id $probe.Id
        if (!$probe.WaitForExit(10000)) {throw 'Test PID still running; do not restore its directories yet.'}
        throw 'Test process did not reach its automatic quit; inspect player/preloader logs.'
    }
    if($probe.ExitCode -ne 0){throw ('Test process failed with exit code '+$probe.ExitCode)}
    if (!(Test-Path -LiteralPath $bepLog) -or (Get-Item -LiteralPath $bepLog).LastWriteTimeUtc -le $oldTime) { throw 'No new BepInEx runtime log.' }
    Copy-Item -LiteralPath $bepLog -Destination (Join-Path $validation ($Label + '-bepinex.log'))
    if (!(Test-Path -LiteralPath $driverReport)) { throw 'No synchronous test-driver report.' }
    Copy-Item -LiteralPath $driverReport -Destination (Join-Path $validation ($Label + '-runtime.log'))
    $report = $bepLog
    if (Test-Path -LiteralPath $report) { Copy-Item -LiteralPath $report -Destination (Join-Path $validation ($Label + '-bridge.log')) }
    Get-Content -LiteralPath $bepLog | Select-String 'WorkshopBridge|BRIDGE_SMOKE|BRIDGE_TEST_SCENE|BRIDGE_TEST_QUIT|Error|Exception|plugins to load'
    if ($StartupOnly) {
        if (!(Select-String -LiteralPath $driverReport -Pattern 'LOADER_STARTUP_ONLY' -Quiet)) {throw 'Missing startup report.'}
    } else {
        if (!(Select-String -LiteralPath $driverReport -Pattern 'BRIDGE_TEST_SCENE=MainMenu STEAM_VALID=True' -Quiet)) { throw 'Did not verify menu and game Steam initialization.' }
        if (!(Select-String -LiteralPath $driverReport -Pattern 'BRIDGE_TEST_QUIT' -Quiet)) { throw 'Missing normal test shutdown marker.' }
    }
} catch {
    $failure = $_
} finally {
    # A diagnostic-copy failure must never prevent stopping the process we started.
    $stopped = !$probe
    if ($probe) {
        try {
            $stopped = $probe.HasExited
            if (!$stopped) { $stopped = $probe.WaitForExit(3000) }
            if (!$stopped) {
                Stop-Process -Id $probe.Id -Force -ErrorAction Stop
                $stopped = $probe.WaitForExit(10000)
            }
            if (!$stopped) { throw 'Test process is still running.' }
        } catch { $cleanupErrors.Add('Cannot confirm test process exit: ' + $_.Exception.Message) }
    }
    # Each diagnostic is best effort; do not hide the original test error.
    if ($validation) {
        foreach ($capture in @(@($bepLog,'bepinex'),@($driverReport,'runtime'),@($loaderReport,'bridge'),@((Join-Path $GameRoot 'BepInEx/config/DriverDiagnostic.log'),'driver-diagnostic'))) {
            try {
                if ($capture[0] -and (Test-Path -LiteralPath $capture[0])) {
                    Copy-Item -LiteralPath $capture[0] -Destination (Join-Path $validation ($Label+'-'+$capture[1]+'.log')) -Force -ErrorAction Stop
                }
            } catch { $cleanupErrors.Add('Cannot retain ' + $capture[1] + ' diagnostic: ' + $_.Exception.Message) }
        }
    }
    # Never remove test files while process exit is unknown.
    if ($stopped -and $driver) {
        foreach ($path in @((Join-Path $driver 'BridgeTestDriver.dll'), $driverReport)) {
            try { if ($path -and (Test-Path -LiteralPath $path)) { Remove-Item -LiteralPath $path -Force -ErrorAction Stop } }
            catch { $cleanupErrors.Add('Cannot remove test artifact: ' + $_.Exception.Message) }
        }
        try {
            if ((Test-Path -LiteralPath $driver) -and (Get-ChildItem -LiteralPath $driver -Force).Count -eq 0) {
                Remove-Item -LiteralPath $driver -ErrorAction Stop
            }
        } catch { $cleanupErrors.Add('Cannot remove test driver directory: ' + $_.Exception.Message) }
    }
}
if ($failure) {
    foreach ($message in $cleanupErrors) { Write-Warning $message -WarningAction Continue }
    throw $failure
}
if ($cleanupErrors.Count) { throw ('Test cleanup/diagnostic retention failed: ' + ($cleanupErrors -join '; ')) }
Write-Output ('PASS ' + $Label)
