param([Parameter(Mandatory=$true)][string]$SandboxRoot)
$ErrorActionPreference='Stop'
$loaderRoot=$PSScriptRoot
$label='cleanup-real-copy-failure'
$state=[pscustomobject]@{Failures=0;Target=[IO.Path]::GetFullPath((Join-Path $loaderRoot "validation/$label-baseline-false-bepinex.log"))}
function Copy-Item {
 param($LiteralPath,$Destination,[switch]$Force,$ErrorAction)
 if([IO.Path]::GetFullPath($Destination) -eq  $state.Target){
  $state.Failures++; throw 'injected diagnostic destination failure'
 }
 Microsoft.PowerShell.Management\Copy-Item -LiteralPath $LiteralPath -Destination $Destination -Force:$Force -ErrorAction Stop
}
$failure=$null
try { & "$PSScriptRoot/Test-SessionProtection.ps1" -SandboxRoot $SandboxRoot -LabelPrefix $label } catch { $failure=$_.Exception.Message }
if($failure -notmatch 'injected diagnostic destination failure' -or $state.Failures -lt 2){throw 'Diagnostic failure injection was not exercised in both the test body and finally'}
if(Get-Process -Name Tailside -ErrorAction SilentlyContinue){throw 'Test process remains alive'}
$result=Get-Content "$loaderRoot/validation/$label-session-protection-results.json" -Raw | ConvertFrom-Json
if(!$result.originalFilesRestored){throw 'Original files not restored'}
if(Test-Path -LiteralPath "$($result.backup)/test-plugins/BridgeTestDriver/BridgeTestDriver.dll"){throw 'Test driver was not removed'}
if(Test-Path -LiteralPath "$($result.backup)/test-config/BridgeTestDriver.report.log"){throw 'Test report was not removed'}
[ordered]@{diagnosticCopyFailures=$state.Failures;originalError=$failure;processStopped=$true;testDllRemoved=$true;testReportRemoved=$true;originalFilesRestored=$true;backup=$result.backup}|ConvertTo-Json|Set-Content "$loaderRoot/validation/cleanup-real-copy-failure-check.json" -Encoding utf8
Write-Output 'PASS real isolated-game diagnostic-copy failure: process stopped, artifacts removed, original files restored by SHA-256, original error retained.'
