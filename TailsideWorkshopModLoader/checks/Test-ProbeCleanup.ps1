$ErrorActionPreference='Stop'
$loaderRoot=Split-Path -Parent $PSScriptRoot
$probeScript=Join-Path $loaderRoot 'Run-IntegrationProbe.ps1'
$passed=0
function Check([bool]$ok,[string]$message){if(!$ok){throw $message};$script:passed++;Write-Output "PASS $message"}
function Run-Case([string]$scenario){
 $root=Join-Path ([IO.Path]::GetTempPath()) ('tailside-cleanup-check-'+[guid]::NewGuid().ToString('N'))
 New-Item -ItemType Directory -Path "$root/BepInEx/config" -Force | Out-Null
 New-Item -ItemType File -Path "$root/.modsetup-sandbox" | Out-Null
 $label='cleanup-unit-'+$scenario
 $state=[pscustomobject]@{Fake=$null;StopCalls=0;WaitCalls=0;CopyAttempts=[Collections.Generic.List[string]]::new()}
 function Get-Process { param($Name,$ErrorAction) }
 function Start-Process {
  param($FilePath,$WorkingDirectory,$ArgumentList,$WindowStyle,[switch]$PassThru)
  [IO.File]::WriteAllText("$root/BepInEx/LogOutput.log",'test log')
  (Get-Item "$root/BepInEx/LogOutput.log").LastWriteTimeUtc=[datetime]::UtcNow.AddSeconds(2)
  [IO.File]::WriteAllText("$root/BepInEx/config/BridgeTestDriver.report.log","BRIDGE_TEST_SCENE=MainMenu STEAM_VALID=True`nBRIDGE_TEST_QUIT")
  [IO.File]::WriteAllText("$root/BepInEx/config/DriverDiagnostic.log",'test diagnostic')
  $state.Fake=[pscustomobject]@{Id=123456789;HasExited=$false;ExitCode=0}
  $state.Fake | Add-Member ScriptMethod WaitForExit {
   param($milliseconds)
   $state.WaitCalls++
   if($scenario -eq 'exception-live' -and $state.WaitCalls -eq 1){throw 'original wait failure'}
   if($scenario -in @('timeout','cannot-stop','exception-live')){return $this.HasExited}
   $this.HasExited=$true;return $true
  }
  return $state.Fake
 }
 function Stop-Process {
  param($Id,[switch]$Force,$ErrorAction)
  if($Id -ne 123456789){throw 'Wrong process target'}
  $state.StopCalls++
  if($scenario -eq 'cannot-stop'){throw 'injected stop failure'}
  $state.Fake.HasExited=$true
 }
 function Copy-Item {
  param($LiteralPath,$Destination,[switch]$Force,$ErrorAction)
  # This is a mocked process test; no prebuilt game test-driver DLL is needed.
  if($LiteralPath -eq (Join-Path $loaderRoot 'TestDriver/bin/Release/netstandard2.1/BridgeTestDriver.dll')){
   [IO.File]::WriteAllText((Join-Path $Destination 'BridgeTestDriver.dll'),'mock test driver')
   return
  }
  if($Destination.StartsWith((Join-Path $loaderRoot 'validation'),[StringComparison]::OrdinalIgnoreCase)){
   $state.CopyAttempts.Add((Split-Path -Leaf $Destination))
   if($scenario -ne 'normal'){throw 'injected log copy failure'}
  }
  Microsoft.PowerShell.Management\Copy-Item -LiteralPath $LiteralPath -Destination $Destination -Force:$Force
 }
 try {
  $errorText=$null; $warnings=@(); $output=@()
  try {$output=@(& $probeScript -GameRoot $root -Label $label -WarningVariable warnings)}catch{$errorText=$_.Exception.Message}
  $driver="$root/BepInEx/plugins/BridgeTestDriver/BridgeTestDriver.dll"
  if($scenario -eq 'normal'){
   Check (!$errorText -and ($output -match '^PASS ')) 'normal run reports success after cleanup'
  }else{Check ([bool]$errorText) "$scenario reports failure"}
  if($scenario -eq 'cannot-stop'){
   Check (Test-Path -LiteralPath $driver) 'unconfirmed live process retains test DLL'
   Check ([bool]($warnings -match 'Cannot confirm test process exit')) 'stop failure remains visible alongside original error'
  }else{
   Check (!(Test-Path -LiteralPath $driver)) "$scenario removes test DLL"
   Check (!(Test-Path -LiteralPath "$root/BepInEx/config/BridgeTestDriver.report.log")) "$scenario removes test report"
  }
  if($scenario -in @('timeout','exception-live')){Check ($state.StopCalls -ge 1 -and $state.Fake.HasExited) "$scenario stops only the owned process despite log errors"}
  if($scenario -eq 'exception-live'){Check ($errorText -match 'original wait failure') 'primary failure is preserved'}
  if($scenario -eq 'timeout'){Check ($errorText -match 'did not reach its automatic quit') 'timeout failure is preserved'}
  Check ([bool]($state.CopyAttempts -match 'driver-diagnostic')) "$scenario attempts remaining diagnostics independently"
 } finally {
  # Only this newly-created mock fixture is removed; no real process was launched.
  $resolved=[IO.Path]::GetFullPath($root)
  if(!$resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or !(Test-Path "$root/.modsetup-sandbox")){throw 'Unsafe mock cleanup path'}
  Remove-Item -LiteralPath $resolved -Recurse -Force
 }
}
foreach($scenario in @('normal','copy-failure','timeout','exception-live','cannot-stop')){Run-Case $scenario}
# Exercise the outer restore guards without creating any real process.
foreach($scriptName in @('Test-SessionProtection.ps1','Test-Runtime.ps1')) {
 & {
  $root=Join-Path $loaderRoot ('validation/tr-'+[guid]::NewGuid().ToString('N').Substring(0,8))
  $state=[pscustomobject]@{Calls=0;Started=$false}
  function Get-Process { param($Name,$ErrorAction); $state.Calls++; if($state.Started){[pscustomobject]@{Id=123456789}} }
  function Copy-Item { param($LiteralPath,$Destination,[switch]$Force); $state.Started=$true; throw 'injected setup failure' }
  try {
   New-Item -ItemType File -Path "$root/.modsetup-sandbox" -Force | Out-Null
   foreach($name in @('patchers','plugins','config','cache')){New-Item -ItemType Directory -Path "$root/BepInEx/$name" -Force | Out-Null; [IO.File]::WriteAllText("$root/BepInEx/$name/original.txt",'preserve')}
   [IO.File]::WriteAllText("$root/BepInEx/config/BepInEx.cfg","[Chainloader]`nHideManagerGameObject = true")
   $errorText=$null
   try { & (Join-Path $PSScriptRoot $scriptName) -SandboxRoot $root | Out-Null } catch { $errorText=$_.Exception.Message }
   Check ($errorText -match 'Game process still present') "$scriptName blocks restore when process exit is unconfirmed"
   $backup=Get-ChildItem -LiteralPath $root -Directory | Where-Object Name -match '(session-protection|loader-test)-backup' | Select-Object -First 1
   foreach($name in @('patchers','plugins','config','cache')) {
    Check ((Test-Path -LiteralPath "$($backup.FullName)/$name/original.txt") -and !(Test-Path -LiteralPath "$root/BepInEx/$name/original.txt")) "$scriptName retains original $name in backup"
   }
  } finally {
   $resolved=[IO.Path]::GetFullPath($root)
   if(!$resolved.StartsWith([IO.Path]::GetFullPath((Join-Path $loaderRoot 'validation')),[StringComparison]::OrdinalIgnoreCase) -or !(Test-Path "$root/.modsetup-sandbox")){throw 'Unsafe mock cleanup path'}
   Remove-Item -LiteralPath $resolved -Recurse -Force
  }
 }
}
Write-Output "Passed $passed cleanup checks. No real game process was launched."
