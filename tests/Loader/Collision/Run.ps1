param([Parameter(Mandatory=$true)][string]$SandboxRoot, [ValidateSet('all','bep','loader')][string]$Mode='all', [switch]$LocationFallback, [ValidatePattern('^[a-zA-Z0-9._-]+$')][string]$LabelPrefix, [switch]$RequireWorkshopLoad)
$ErrorActionPreference='Stop'
$SandboxRoot=[IO.Path]::GetFullPath($SandboxRoot).TrimEnd('\')
if(!(Test-Path -LiteralPath "$SandboxRoot/.modsetup-sandbox") -or $SandboxRoot.Contains('steamapps',[StringComparison]::OrdinalIgnoreCase)){throw 'Isolated marked copy required'}
if(Get-Process Tailside -ErrorAction SilentlyContinue){throw 'Game already running'}
$loader=Split-Path -Parent $PSScriptRoot
$bep="$SandboxRoot/BepInEx"
$backup="$SandboxRoot/collision-backup-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $backup | Out-Null
$seed=[IO.File]::ReadAllText("$bep/config/BepInEx.cfg")
function State {
 $s=[ordered]@{}; foreach($n in @('patchers','plugins','config','cache','TailsideWorkshopModLoader')){foreach($f in Get-ChildItem "$bep/$n" -Recurse -File -ErrorAction SilentlyContinue | Sort-Object FullName){$s[[IO.Path]::GetRelativePath($bep,$f.FullName)]=(Get-FileHash $f.FullName).Hash}}; $s|ConvertTo-Json -Compress
}
$before=State
$oldSandbox=$env:MODSETUP_SANDBOX_ROOT; $oldTest=$env:COLLISION_TEST
$oldFallback=$env:COLLISION_TEST_LOCATION_FALLBACK
$moved=@(); $rows=@()
try {
 foreach($n in @('patchers','plugins','config','cache','TailsideWorkshopModLoader')){if(Test-Path -LiteralPath "$bep/$n"){Move-Item -LiteralPath "$bep/$n" -Destination "$backup/$n"}; $moved+=$n}
 $env:MODSETUP_SANDBOX_ROOT=$SandboxRoot; $env:COLLISION_TEST='1'; $env:COLLISION_TEST_LOCATION_FALLBACK=if($LocationFallback){'1'}else{'0'}
 $cases=@(
  @{Name='a-only';A='A-1.0.0.0';B=$null},
  @{Name='b-only';A=$null;B='B-1.0.0.0'},
  @{Name='same-a-first';A='A-1.0.0.0';B='B-1.0.0.0'},
  @{Name='same-b-first';A='A-reverse';B='B-1.0.0.0'},
  @{Name='versions-a-first';A='A-1.0.0.0';B='B-2.0.0.0'},
  @{Name='versions-b-first';A='A-reverse';B='B-2.0.0.0'}
 )
 $modes=if($Mode -eq 'all'){@('bep','loader')}else{@($Mode)}; foreach($mode in $modes){foreach($c in $cases){
  foreach($n in @('patchers','plugins','config','cache','TailsideWorkshopModLoader')){
   $target=[IO.Path]::GetFullPath("$bep/$n")
   if(!$target.StartsWith($SandboxRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe cleanup path'}
   if(Test-Path -LiteralPath $target){Remove-Item -LiteralPath $target -Recurse -Force}
   New-Item -ItemType Directory -Path $target | Out-Null
  }
  New-Item -ItemType Directory -Path "$bep/patchers/Guard" | Out-Null
  Copy-Item "$loader/../SandboxGuard/bin/Release/netstandard2.1/SetupSandboxGuard.dll" "$bep/patchers/Guard"
  [IO.File]::WriteAllText("$bep/config/BepInEx.cfg",[regex]::Replace($seed,'(?m)^HideManagerGameObject\s*=\s*(true|false)','HideManagerGameObject = true'))
  $prefix=if($LabelPrefix){$LabelPrefix}elseif($LocationFallback){'collision-adapted'}else{'collision-raw'}; $label=$prefix+'-'+$mode+'-'+$c.Name
  $items=@()
  foreach($which in @('A','B')){
   $fixture=$c[$which]; if(!$fixture){continue}
   $dest=if($mode -eq 'bep'){"$bep/plugins/$which"}else{"$backup/items/$label/$which/plugins"}
   New-Item -ItemType Directory -Path $dest -Force | Out-Null
   Copy-Item "$PSScriptRoot/fixtures/$fixture/CollisionMod$which.dll","$PSScriptRoot/fixtures/$fixture/CollisionHelper.dll" $dest
   if($mode -eq 'loader'){
    $root=Split-Path -Parent $dest
    $id=if($fixture -eq 'A-reverse'){'collision.z'}else{'collision.'+$which.ToLowerInvariant()}
    @{schemaVersion=1;id=$id;name="Collision $which";version='1.0.0';gameAppId=2922570;entryAssembly="plugins/CollisionMod$which.dll";minimumLoaderVersion='1.0.0'}|ConvertTo-Json|Set-Content "$root/mod.json" -Encoding utf8
    $items+=$root
   }
  }
  if($mode -eq 'loader'){
   New-Item -ItemType Directory -Path "$bep/plugins/ManualProbe" | Out-Null
   Copy-Item "$loader/SmokeProbe/bin/Release/netstandard2.1/BridgeSmokeProbe.dll" "$bep/plugins/ManualProbe"
   New-Item -ItemType Directory -Path "$bep/patchers/Injector" -Force | Out-Null
   Copy-Item "$loader/../../TailsideWorkshopModLoader/release/BepInEx/patchers/TailsideWorkshopModLoader.dll" "$bep/patchers"
   Copy-Item "$PSScriptRoot/Injector/bin/Release/netstandard2.1/CollisionTestInjector.dll" "$bep/patchers/Injector"
   $items|Set-Content "$SandboxRoot/.collision-items" -Encoding utf8
  }
  & "$loader/Run-IntegrationProbe.ps1" -GameRoot $SandboxRoot -Label $label -WithGraphics -InteractiveSandbox
  $log=[IO.File]::ReadAllText("$loader/validation/$label-bepinex.log")
  if($mode -eq 'loader'){
   Copy-Item "$bep/config/CollisionTest.diagnostics.log" "$loader/validation/$label-diagnostics.log"
   if($RequireWorkshopLoad){
    $diagnostic=[IO.File]::ReadAllText("$bep/config/CollisionTest.diagnostics.log")
    if($LocationFallback -or $diagnostic -notmatch 'LocationFallback=False' -or $diagnostic -notmatch 'InspectorPatched=False' -or $diagnostic -notmatch 'UnityLocationEmpty=True'){throw 'Unpatched memory-loaded Unity condition not verified'}
    $runtime=[IO.File]::ReadAllText("$loader/validation/$label-runtime.log")
    if($runtime -notmatch 'BRIDGE_TEST_PLUGIN local.workshopbridge.smoke .*alive=True .*ManualProbe'){throw 'Normal plugins did not survive'}
   }
  }
  $ok=[regex]::Matches($log,'COLLISION_OK').Count; $failed=[regex]::Matches($log,'COLLISION_FAIL').Count
  if($ok+$failed -ne $items.Count -and $mode -eq 'loader' -and ($LocationFallback -or $RequireWorkshopLoad)){throw "Missing plugin observation: $label"}
  if($RequireWorkshopLoad -and $c.Name -match '-only$' -and ($ok -ne 1 -or $failed -ne 0)){throw "Single package failed: $label"}
  if($mode -eq 'bep' -and $ok+$failed -ne @($c.A,$c.B | Where-Object {$_}).Count){throw "Missing plugin observation: $label"}
  $rows+=@{case=$label;ok=$ok;failed=$failed;evidence=@($log -split "`n" | Where-Object {$_ -match 'COLLISION_|Loading \[Collision|Candidate collision'})}
  $rows|ConvertTo-Json -Depth 5|Set-Content "$loader/validation/${prefix}-results.json" -Encoding utf8
 }}
} finally {
 $env:MODSETUP_SANDBOX_ROOT=$oldSandbox; $env:COLLISION_TEST=$oldTest; $env:COLLISION_TEST_LOCATION_FALLBACK=$oldFallback
 if(Get-Process Tailside -ErrorAction SilentlyContinue){throw "Game remains alive; original files retained in $backup; do not restore while running"}
 foreach($n in $moved){if(Test-Path -LiteralPath "$bep/$n"){Move-Item -LiteralPath "$bep/$n" -Destination "$backup/test-$n"}; if(Test-Path -LiteralPath "$backup/$n"){Move-Item -LiteralPath "$backup/$n" -Destination "$bep/$n"}}
 if(Test-Path "$SandboxRoot/.collision-items"){Remove-Item -LiteralPath "$SandboxRoot/.collision-items"}
 if((State) -ne $before){throw 'Original state mismatch'}
 @{restored=$true;backup=$backup;cases=$rows.Count;locationFallback=[bool]$LocationFallback;requireWorkshopLoad=[bool]$RequireWorkshopLoad}|ConvertTo-Json|Set-Content "$loader/validation/${prefix}-restoration.json" -Encoding utf8
 Write-Output "PASS original patchers/plugins/config/cache restored by SHA256; backup=$backup"
}



