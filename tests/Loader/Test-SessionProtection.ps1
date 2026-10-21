param([Parameter(Mandatory=$true)][string]$SandboxRoot,[ValidatePattern('^[a-zA-Z0-9._-]+$')][string]$LabelPrefix='v032')
$ErrorActionPreference='Stop'
$SandboxRoot=[IO.Path]::GetFullPath($SandboxRoot).TrimEnd('\')
if(!(Test-Path -LiteralPath "$SandboxRoot/.modsetup-sandbox") -or $SandboxRoot.Contains('steamapps',[StringComparison]::OrdinalIgnoreCase)){throw 'Marked isolated game copy only.'}
if(Get-Process -Name Tailside -ErrorAction SilentlyContinue){throw 'Tailside is running.'}
$loaderRoot=$PSScriptRoot
$bep=Join-Path $SandboxRoot 'BepInEx'
$backup=Join-Path $SandboxRoot ('session-protection-backup-'+[guid]::NewGuid().ToString('N'))
if(![IO.Path]::GetFullPath($backup).StartsWith($SandboxRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Backup outside sandbox.'}
$seedConfig=[IO.File]::ReadAllText("$bep/config/BepInEx.cfg")
$script:sessionSeedConfig=$seedConfig+"`n[SessionProtectionTest]`nUnrelatedSetting = preserve-me`n"
if($seedConfig -notmatch '(?m)^HideManagerGameObject\s*=\s*(true|false)\s*$'){throw 'A complete generated BepInEx.cfg is required.'}
function Get-OriginalState {
    $state=[ordered]@{}
    foreach($name in @('patchers','plugins','config','cache','TailsideModSetup')){
        foreach($file in Get-ChildItem -LiteralPath "$bep/$name" -Recurse -File -ErrorAction SilentlyContinue | Sort-Object FullName){
            $state[[IO.Path]::GetRelativePath($bep,$file.FullName)]=(Get-FileHash -LiteralPath $file.FullName).Hash
        }
    }
    return ($state|ConvertTo-Json -Compress)
}
$originalState=Get-OriginalState
$previousSandbox=$env:MODSETUP_SANDBOX_ROOT
$previousTrace=$env:MODSETUP_TRACE_STARTUP
$moved=@()
$results=[Collections.Generic.List[object]]::new()
New-Item -ItemType Directory -Path $backup -Force | Out-Null
try {
    foreach($name in @('patchers','plugins','config','cache','TailsideModSetup')){
        if(Test-Path -LiteralPath "$bep/$name"){Move-Item -LiteralPath "$bep/$name" -Destination "$backup/$name"}
        $moved+=$name
        New-Item -ItemType Directory -Path "$bep/$name" | Out-Null
    }
    New-Item -ItemType Directory -Path "$bep/patchers/SetupSandboxGuard","$bep/plugins/ManualProbe" -Force | Out-Null
    Copy-Item -LiteralPath "$loaderRoot/../SandboxGuard/bin/Release/netstandard2.1/SetupSandboxGuard.dll" -Destination "$bep/patchers/SetupSandboxGuard"
    Copy-Item -LiteralPath "$loaderRoot/SmokeProbe/bin/Release/netstandard2.1/BridgeSmokeProbe.dll" -Destination "$bep/plugins/ManualProbe"
    $env:MODSETUP_SANDBOX_ROOT=$SandboxRoot
    $env:MODSETUP_TRACE_STARTUP='0'
    $target="$bep/patchers/TailsideWorkshopModLoader.dll"
    function Invoke-Case([string]$name,[bool]$withLoader,[bool]$hide,[bool]$expectAlive){
        if($withLoader){Copy-Item -LiteralPath "$loaderRoot/../../TailsideWorkshopModLoader/release/BepInEx/patchers/TailsideWorkshopModLoader.dll" -Destination $target -Force}
        elseif(Test-Path -LiteralPath $target){Move-Item -LiteralPath $target -Destination "$backup/$name-removed-loader.dll"}
        $config=[regex]::Replace($script:sessionSeedConfig,'(?m)^HideManagerGameObject[ \t]*=[ \t]*(true|false)',('HideManagerGameObject = '+$hide.ToString().ToLowerInvariant()))
        [IO.File]::WriteAllText("$bep/config/BepInEx.cfg",$config,[Text.UTF8Encoding]::new($false))
        if(Test-Path -LiteralPath "$bep/config/DriverDiagnostic.log"){Remove-Item -LiteralPath "$bep/config/DriverDiagnostic.log"}
        $before=(Get-FileHash -LiteralPath "$bep/config/BepInEx.cfg").Hash
        $writeTime=(Get-Item -LiteralPath "$bep/config/BepInEx.cfg").LastWriteTimeUtc
        & "$loaderRoot/Run-IntegrationProbe.ps1" -GameRoot $SandboxRoot -WithGraphics -InteractiveSandbox -Label $name
        $report=Get-Content "$loaderRoot/validation/$name-runtime.log" -Raw
        $after=(Get-FileHash -LiteralPath "$bep/config/BepInEx.cfg").Hash
        $nativeResaved=(Get-Item -LiteralPath "$bep/config/BepInEx.cfg").LastWriteTimeUtc -ne $writeTime
        if($name -eq ($LabelPrefix+'-baseline-false')){
            # BepInEx itself reserializes non-default settings on binding; normalize with no loader.
            $script:sessionSeedConfig=[IO.File]::ReadAllText("$bep/config/BepInEx.cfg")
        }elseif($before -ne $after){throw "Configuration contents changed beyond the native baseline: $name"}
        if([IO.File]::ReadAllText("$bep/config/BepInEx.cfg") -notmatch ('(?m)^HideManagerGameObject = '+$hide.ToString().ToLowerInvariant())){throw "HideManager setting changed: $name"}
        $alive=$expectAlive.ToString()
        if($report -notmatch "MANAGER_ALIVE=$alive\b" -or $report -notmatch "DRIVER_STARTED=$alive\b" -or $report -notmatch "BRIDGE_TEST_PLUGIN local.workshopbridge.smoke .*alive=$alive .*ManualProbe"){throw "Unexpected plugin lifecycle: $name"}
        if($expectAlive -and $report -notmatch 'MANAGER_ALIVE=True FLAGS=61'){throw "Missing manager flags: $name"}
        $loaderLog="$bep/LogOutput.log"
        if($withLoader){
            if(!(Test-Path -LiteralPath $loaderLog) -or (Get-Content $loaderLog -Raw) -notmatch 'Applied session-only BepInEx manager protection before plugin creation'){throw "No session protection: $name"}
        }elseif((Get-Content $loaderLog -Raw) -match "\[.*TailsideWorkshopModLoader\]"){throw "Removed loader still running: $name"}
        $results.Add([ordered]@{case=$name;loader=$withLoader;configHide=$hide;managerAlive=$expectAlive;configBeforeSHA256=$before;configAfterSHA256=$after;configContentsUnchanged=($before -eq $after);nativeConfigResaved=$nativeResaved;exitCode=0;mainMenuAndSteam=$true})
        Write-Output "PASS lifecycle and preserved configuration values: $name"
    }
    Invoke-Case ($LabelPrefix+'-baseline-false') $false $false $false
    Invoke-Case ($LabelPrefix+'-loader-false') $true $false $true
    Invoke-Case ($LabelPrefix+'-removed-false') $false $false $false
    Invoke-Case ($LabelPrefix+'-loader-true') $true $true $true
    Invoke-Case ($LabelPrefix+'-removed-true') $false $true $true
} finally {
    $env:MODSETUP_SANDBOX_ROOT=$previousSandbox
    $env:MODSETUP_TRACE_STARTUP=$previousTrace
    if (Get-Process -Name Tailside -ErrorAction SilentlyContinue) {
        throw "Game process still present; do not restore directories while running. Original files retained in $backup"
    }
    foreach($name in $moved){
        if(Test-Path -LiteralPath "$bep/$name"){Move-Item -LiteralPath "$bep/$name" -Destination "$backup/test-$name"}
        if(Test-Path -LiteralPath "$backup/$name"){Move-Item -LiteralPath "$backup/$name" -Destination "$bep/$name"}
    }
    $restored=(Get-OriginalState) -eq $originalState
    [ordered]@{date='2026-10-20';cases=$results.ToArray();originalFilesRestored=$restored;backup=$backup}|ConvertTo-Json -Depth 5|Set-Content "$loaderRoot/validation/${LabelPrefix}-session-protection-results.json" -Encoding utf8
    if(!$restored){throw 'Original sandbox files not exactly restored.'}
    Write-Output "PASS original sandbox files restored by SHA-256. Test artifacts: $backup"
}


