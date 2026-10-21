param([Parameter(Mandatory=$true)][string]$GameRoot)
$ErrorActionPreference='Stop'
$GameRoot=[IO.Path]::GetFullPath($GameRoot).TrimEnd('\')
if(!(Test-Path "$GameRoot/.modsetup-sandbox") -or $GameRoot.Contains('steamapps',[StringComparison]::OrdinalIgnoreCase)){throw 'Marked isolated copy only'}
if(Get-Process Tailside -ErrorAction SilentlyContinue){throw 'Game running'}
$tool=Split-Path -Parent $PSScriptRoot
$loader=Join-Path $tool '../TailsideWorkshopModLoader'
$runner=Join-Path $tool '../ModEnvironmentSetup/Integration/bin/Release/net10.0/Integration.dll'
$app=Join-Path $tool 'App/bin/Release/net10.0-windows/win-x64/TailsideModSetup.dll'
$backup=Join-Path $GameRoot ('current-payload-backup-'+[guid]::NewGuid().ToString('N'))
if(![IO.Path]::GetFullPath($backup).StartsWith($GameRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe backup path'}
$paths=@('BepInEx','winhttp.dll','doorstop_config.ini','.doorstop_version','winhttp.dll.modmanager-disabled')
function Snapshot {
 $s=[ordered]@{}
 foreach($n in $paths){$p=Join-Path $GameRoot $n;if(Test-Path $p){if((Get-Item $p).PSIsContainer){foreach($f in Get-ChildItem $p -Recurse -File|Sort-Object FullName){$s[[IO.Path]::GetRelativePath($GameRoot,$f.FullName)]=(Get-FileHash $f.FullName).Hash}}else{$s[$n]=(Get-FileHash $p).Hash}}}
 $s|ConvertTo-Json -Compress
}
$before=Snapshot
$exeHash=(Get-FileHash "$GameRoot/Tailside.exe").Hash
$previous=$env:MODSETUP_SANDBOX_ROOT
$moved=@();$verified=$false;New-Item -ItemType Directory -Path $backup|Out-Null
try {
 foreach($n in $paths){if(Test-Path -LiteralPath "$GameRoot/$n"){Move-Item -LiteralPath "$GameRoot/$n" -Destination "$backup/$n";$moved+=$n}}
 dotnet $runner install $GameRoot $app
 if($LASTEXITCODE){throw 'Embedded payload installation failed'}
 $installed="$GameRoot/BepInEx/patchers/TailsideWorkshopModLoader.dll"
 if(!(Test-Path $installed) -or (Get-FileHash $installed).Hash -ne (Get-FileHash "$loader/release/BepInEx/patchers/TailsideWorkshopModLoader.dll").Hash){throw 'Wrong installed loader'}
 if(Test-Path "$GameRoot/BepInEx/patchers/WorkshopBridge/WorkshopBridge.dll"){throw 'Legacy loader present'}
 New-Item -ItemType Directory -Path "$GameRoot/BepInEx/patchers/Guard","$GameRoot/BepInEx/plugins/ManualProbe"|Out-Null
 Copy-Item "$tool/../ModEnvironmentSetup/SandboxGuard/bin/Release/netstandard2.1/SetupSandboxGuard.dll" "$GameRoot/BepInEx/patchers/Guard"
 Copy-Item "$loader/SmokeProbe/bin/Release/netstandard2.1/BridgeSmokeProbe.dll" "$GameRoot/BepInEx/plugins/ManualProbe"
 $env:MODSETUP_SANDBOX_ROOT=$GameRoot
 & "$loader/Run-IntegrationProbe.ps1" -GameRoot $GameRoot -Label current-exe-payload -WithGraphics -InteractiveSandbox
 $runtime=[IO.File]::ReadAllText("$loader/validation/current-exe-payload-runtime.log")
 if($runtime -notmatch 'BRIDGE_TEST_PLUGIN local.workshopbridge.smoke .*alive=True'){throw 'Normal plugin did not survive'}
 # Temporarily remove only the ownership record to exercise manual-mode UI on real game files.
 $receipt="$GameRoot/BepInEx/TailsideModSetup/data/install.json"
 Move-Item -LiteralPath $receipt -Destination "$backup/owned-receipt.json"
 try {
  $ui=Join-Path $tool 'UiChecks/bin/Release/net10.0-windows/win-x64/UiChecks.exe'
  $p=Start-Process -FilePath $ui -ArgumentList @('actual-manual','--game-dir',('"'+$GameRoot+'"'),'--ui-check') -WindowStyle Hidden -PassThru -RedirectStandardOutput "$tool/validation/recovery-actual-manual.log" -RedirectStandardError "$tool/validation/recovery-actual-manual.error.log"
  if(!$p.WaitForExit(30000)){Stop-Process -Id $p.Id -Force;$p.WaitForExit(5000)|Out-Null;throw 'Manual UI test timed out'}
  Get-Content "$tool/validation/recovery-actual-manual.log"
  if($p.ExitCode -ne 0){Get-Content "$tool/validation/recovery-actual-manual.error.log";throw 'Actual manual UI test failed'}
 } finally {
  if($p -and !$p.HasExited){throw "UI test process still running; ownership record retained in $backup"}
  Move-Item -LiteralPath "$backup/owned-receipt.json" -Destination $receipt
 }
 dotnet $runner remove $GameRoot $app
 if($LASTEXITCODE){throw 'Embedded payload removal failed'}
 if((Test-Path $installed) -or (Test-Path "$GameRoot/winhttp.dll")){throw 'Owned loader or injector remains'}
 if(!(Test-Path "$GameRoot/BepInEx/plugins/ManualProbe/BridgeSmokeProbe.dll")){throw 'Unowned mod removed'}
 if((Get-FileHash "$GameRoot/Tailside.exe").Hash -ne $exeHash){throw 'Game executable changed'}
 $verified=$true
} finally {
 $env:MODSETUP_SANDBOX_ROOT=$previous
 if(Get-Process Tailside -ErrorAction SilentlyContinue){throw "Game remains active; originals retained at $backup"}
 if($p -and !$p.HasExited){throw "UI test process still running; originals retained at $backup"}
 foreach($n in $paths){if(Test-Path -LiteralPath "$GameRoot/$n"){Move-Item -LiteralPath "$GameRoot/$n" -Destination "$backup/test-$n"}}
 foreach($n in $moved){Move-Item -LiteralPath "$backup/$n" -Destination "$GameRoot/$n"}
 if((Snapshot) -ne $before){throw 'Original files not restored'}
 [ordered]@{version='loader 1.0.0';completed=$verified;originalFilesRestored=$true;backup=$backup}|ConvertTo-Json|Set-Content "$tool/validation/current-payload-results.json"
 Write-Output "PASS original isolated files restored by SHA256; backup=$backup"
}


