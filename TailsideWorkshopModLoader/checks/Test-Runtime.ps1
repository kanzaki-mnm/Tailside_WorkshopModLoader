param([Parameter(Mandatory=$true)][string]$SandboxRoot,[switch]$NoLoader,[switch]$FullStartup,[switch]$WithGraphics,[bool]$HideManager=$true,[switch]$TraceStartup,[ValidateRange(10,60)][int]$TimeoutSeconds=35)
$ErrorActionPreference='Stop'
$SandboxRoot=[IO.Path]::GetFullPath($SandboxRoot).TrimEnd('\')
if(!(Test-Path -LiteralPath "$SandboxRoot/.modsetup-sandbox") -or $SandboxRoot.Contains('steamapps',[StringComparison]::OrdinalIgnoreCase)){throw 'Marked isolated copy only.'}
if(Get-Process -Name Tailside -ErrorAction SilentlyContinue){throw 'Tailside is running.'}
$loaderRoot=Split-Path -Parent $PSScriptRoot
$backup=Join-Path $SandboxRoot ('loader-test-backup-'+[guid]::NewGuid().ToString('N'))
$bep=Join-Path $SandboxRoot 'BepInEx'
$previousSandbox=$env:MODSETUP_SANDBOX_ROOT
$previousTrace=$env:MODSETUP_TRACE_STARTUP
$moved=@()
New-Item -ItemType Directory -Path $backup -Force | Out-Null
try {
    foreach($name in @('patchers','plugins','config','cache','TailsideModSetup')){
        $path=Join-Path $bep $name
        if(Test-Path -LiteralPath $path){Move-Item -LiteralPath $path -Destination "$backup/$name"; }
        $moved+= $name
        New-Item -ItemType Directory -Path $path -Force | Out-Null
    }
    New-Item -ItemType Directory -Path "$bep/patchers/SetupSandboxGuard","$bep/plugins/ManualProbe","$backup/only-local/plugins" -Force | Out-Null
    Copy-Item -LiteralPath "$loaderRoot/../ModEnvironmentSetup/SandboxGuard/bin/Release/netstandard2.1/SetupSandboxGuard.dll" -Destination "$bep/patchers/SetupSandboxGuard"
    Copy-Item -LiteralPath "$loaderRoot/release/BepInEx/patchers/TailsideWorkshopModLoader.dll" -Destination "$bep/patchers"
    $manual="$bep/plugins/ManualProbe/BridgeSmokeProbe.dll"
    Copy-Item -LiteralPath "$loaderRoot/SmokeProbe/bin/Release/netstandard2.1/BridgeSmokeProbe.dll" -Destination $manual
    Copy-Item -LiteralPath $manual -Destination "$backup/only-local/plugins"
    '{"schemaVersion":1,"id":"local.workshopbridge.smoke","name":"Local-only probe","version":"1.0.0","gameAppId":2922570,"entryAssembly":"plugins/BridgeSmokeProbe.dll","minimumLoaderVersion":"0.3.0"}' | Set-Content -LiteralPath "$backup/only-local/mod.json"
    "[Development]`nEnabled = true`nLocalRoot = $backup`nOverrideIds = local.workshopbridge.smoke" | Set-Content -LiteralPath "$bep/config/WorkshopBridge.cfg"
    '{"schemaVersion":1,"pauseAll":true,"disabledIds":["local.workshopbridge.smoke"]}' | Set-Content -LiteralPath "$bep/config/WorkshopBridge.mods.json"
    "[Chainloader]`nHideManagerGameObject = $($HideManager.ToString().ToLowerInvariant())`n[Logging.Disk]`nEnabled = true`nInstantFlushing = true`n[Preloader.Entrypoint]`nAssembly = UnityEngine.CoreModule.dll`nType = Application`nMethod = .cctor" | Set-Content -LiteralPath "$bep/config/BepInEx.cfg"
    $env:MODSETUP_SANDBOX_ROOT=$SandboxRoot
    $env:MODSETUP_TRACE_STARTUP=if($TraceStartup){'1'}else{'0'}
    if(Test-Path "$SandboxRoot/startup-trace.log"){Move-Item "$SandboxRoot/startup-trace.log" "$backup/previous-startup-trace.log"}
    $runOptions=@{GameRoot=$SandboxRoot;StartupOnly=(!$FullStartup);WithGraphics=[bool]$WithGraphics;InteractiveSandbox=[bool]$WithGraphics;TimeoutSeconds=$TimeoutSeconds}
    if($NoLoader){
        Move-Item -LiteralPath "$bep/patchers/TailsideWorkshopModLoader.dll" -Destination "$backup/unused-loader.dll"
        $baselineLabel=if($HideManager){'v030-baseline-hidden-manager'}else{'v030-baseline-visible-manager'}
        & "$loaderRoot/Run-IntegrationProbe.ps1" @runOptions -Label $baselineLabel
        Write-Output 'PASS baseline without loader'; return
    }
    & "$loaderRoot/Run-IntegrationProbe.ps1" @runOptions -Label 'v030-manual-with-loader'
    $log=Get-Content "$loaderRoot/validation/v030-manual-with-loader-bepinex.log" -Raw
    $report=Get-Content "$loaderRoot/validation/v030-manual-with-loader-runtime.log" -Raw
    if($log -notmatch 'SANDBOX_ISOLATED' -or $log -notmatch 'TailsideWorkshopModLoader 0.3.2 ready' -or $report -notmatch 'BRIDGE_TEST_PLUGIN local.workshopbridge.smoke .*ManualProbe'){throw 'Manual plugins or sandbox guard failed.'}
    if(Test-Path -LiteralPath "$bep/config/TailsideWorkshopModLoader.cfg"){throw 'Unexpected loader configuration.'}
    Move-Item -LiteralPath $manual -Destination "$backup/manual-probe.dll"
    & "$loaderRoot/Run-IntegrationProbe.ps1" @runOptions -Label 'v030-no-local-discovery'
    $report=Get-Content "$loaderRoot/validation/v030-no-local-discovery-runtime.log" -Raw
    if($report -match 'BRIDGE_TEST_PLUGIN local.workshopbridge.smoke '){throw 'LocalRoot was unexpectedly loaded.'}
    Move-Item -LiteralPath "$backup/manual-probe.dll" -Destination $manual
    Move-Item -LiteralPath "$bep/patchers/TailsideWorkshopModLoader.dll" -Destination "$backup/removed-loader.dll"
    & "$loaderRoot/Run-IntegrationProbe.ps1" @runOptions -Label 'v030-loader-removed'
    $report=Get-Content "$loaderRoot/validation/v030-loader-removed-runtime.log" -Raw
    if($report -notmatch 'BRIDGE_TEST_PLUGIN local.workshopbridge.smoke .*ManualProbe'){throw 'Manual plugins failed after loader removal.'}
    if((Get-Content "$bep/LogOutput.log" -Raw) -match "\[.*TailsideWorkshopModLoader\]"){throw 'Removed loader still executed.'}
    Write-Output 'PASS normal plugins, removed development path, loader removal; no save opened.'
} finally {
    $env:MODSETUP_SANDBOX_ROOT=$previousSandbox
    $env:MODSETUP_TRACE_STARTUP=$previousTrace
    if (Get-Process -Name Tailside -ErrorAction SilentlyContinue) {
        throw "Game process still present; do not restore directories while running. Original files retained in $backup"
    }
    foreach($name in $moved){
        $path=Join-Path $bep $name
        # Preserve test outputs, then restore the pre-test directories. No recursive deletion.
        if(Test-Path -LiteralPath $path){Move-Item -LiteralPath $path -Destination "$backup/test-$name"}
        if(Test-Path -LiteralPath "$backup/$name"){Move-Item -LiteralPath "$backup/$name" -Destination $path}
    }
    Write-Output "Sandbox directories restored; test artifacts: $backup"
}



