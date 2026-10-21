param([Parameter(Mandatory=$true)][string]$GameRoot,[string]$ToolExe)
$ErrorActionPreference='Stop'
$GameRoot=[IO.Path]::GetFullPath($GameRoot).TrimEnd('\')
if($GameRoot.Contains('steamapps',[StringComparison]::OrdinalIgnoreCase) -or !(Test-Path -LiteralPath "$GameRoot/.modsetup-sandbox")){throw 'Marked isolated game copy only'}
if(Get-Process Tailside,TailsideModSetup -ErrorAction SilentlyContinue){throw 'Game or tool running'}
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$backup=Join-Path $repo ('.local/backups/Tools-lifecycle-'+[guid]::NewGuid().ToString('N'))
$tool=if($ToolExe){[IO.Path]::GetFullPath($ToolExe)}else{Join-Path $repo 'TailsideModSetup/release-preview/TailsideModSetup.exe'}
if(!(Test-Path -LiteralPath $tool -PathType Leaf)){throw 'Tool executable not found'}
Write-Output ('Testing supplied executable SHA256='+ (Get-FileHash -LiteralPath $tool).Hash)
$driver=Join-Path $repo 'tests/Setup/LifecycleChecks/bin/Release/net10.0-windows/LifecycleChecks.dll'
$work=Join-Path $backup 'downloads'
$paths=@('BepInEx','winhttp.dll','doorstop_config.ini','.doorstop_version','winhttp.dll.modmanager-disabled')
function Snapshot {
 $s=[ordered]@{};foreach($name in $paths){$p=Join-Path $GameRoot $name;if(Test-Path -LiteralPath $p){if((Get-Item -LiteralPath $p).PSIsContainer){foreach($f in Get-ChildItem -LiteralPath $p -Recurse -File|Sort-Object FullName){$s[[IO.Path]::GetRelativePath($GameRoot,$f.FullName)]=(Get-FileHash -LiteralPath $f.FullName).Hash}}else{$s[$name]=(Get-FileHash -LiteralPath $p).Hash}}};return ($s|ConvertTo-Json -Compress)
}
$before=Snapshot;$exeHash=(Get-FileHash -LiteralPath "$GameRoot/Tailside.exe").Hash
$moved=@();$passed=$false;$oldSandbox=$env:MODSETUP_SANDBOX_ROOT
New-Item -ItemType Directory -Path $backup -Force|Out-Null
try {
 foreach($name in $paths){$src=[IO.Path]::GetFullPath((Join-Path $GameRoot $name));$dst=[IO.Path]::GetFullPath((Join-Path $backup $name));if(!$src.StartsWith($GameRoot+'\',[StringComparison]::OrdinalIgnoreCase) -or !$dst.StartsWith($backup+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe isolated move'};if(Test-Path -LiteralPath $src){Move-Item -LiteralPath $src -Destination $dst;$moved+=$name}}
 dotnet $driver install $tool $GameRoot $work
 if($LASTEXITCODE){throw 'Published exe installation/copy/relaunch failed'}
 dotnet $driver update $tool $GameRoot $work
 if($LASTEXITCODE){throw 'Published exe update/copy/settings preservation failed'}
 New-Item -ItemType Directory -Path "$GameRoot/BepInEx/patchers/Guard","$GameRoot/BepInEx/plugins/ManualProbe" -Force|Out-Null
 Copy-Item -LiteralPath "$repo/tests/SandboxGuard/bin/Release/netstandard2.1/SetupSandboxGuard.dll" -Destination "$GameRoot/BepInEx/patchers/Guard"
 Copy-Item -LiteralPath "$repo/tests/Loader/SmokeProbe/bin/Release/netstandard2.1/BridgeSmokeProbe.dll" -Destination "$GameRoot/BepInEx/plugins/ManualProbe"
 $env:MODSETUP_SANDBOX_ROOT=$GameRoot
 & "$repo/tests/Loader/Run-IntegrationProbe.ps1" -GameRoot $GameRoot -Label tools-lifecycle -WithGraphics -InteractiveSandbox
 $report=Get-Content -Raw -LiteralPath "$repo/tests/Loader/validation/tools-lifecycle-runtime.log"
 if($report -notmatch 'BRIDGE_TEST_PLUGIN local.workshopbridge.smoke .*alive=True'){throw 'Normal plugin not alive'}
 if((Get-Content -LiteralPath "$GameRoot/BepInEx/LogOutput.log" -Raw) -notmatch 'Discovery complete:'){throw 'Missing loader diagnostics in BepInEx log'}
 if(Test-Path -LiteralPath "$GameRoot/BepInEx/TailsideModSetup/logs/last-run.log"){throw 'Independent loader log unexpectedly created'}
 Write-Output 'PASS actual game MainMenu/Steam/normal plugin and standard BepInEx loader log.'

 dotnet $driver remove $tool $GameRoot $work
 if($LASTEXITCODE){throw 'Redownloaded exe removal/guidance failed'}
 $folder=[IO.Path]::GetFullPath("$GameRoot/BepInEx/TailsideModSetup")
 if(!$folder.StartsWith($GameRoot+'\',[StringComparison]::OrdinalIgnoreCase) -or $folder -ne [IO.Path]::GetFullPath((Join-Path $GameRoot 'BepInEx/TailsideModSetup'))){throw 'Unsafe manual cleanup path'}
 if(Get-Process Tailside,TailsideModSetup -ErrorAction SilentlyContinue){throw 'Process still active before simulated user cleanup'}
 Remove-Item -LiteralPath $folder -Recurse -Force
 if(Test-Path -LiteralPath $folder){throw 'Manual tool-folder deletion failed'}
 if(!(Test-Path -LiteralPath "$GameRoot/BepInEx/plugins/ManualProbe/BridgeSmokeProbe.dll") -or !(Test-Path -LiteralPath "$GameRoot/BepInEx/config/BepInEx.cfg")){throw 'Manual cleanup affected user files'}
 if((Get-FileHash -LiteralPath "$GameRoot/Tailside.exe").Hash -ne $exeHash){throw 'Game executable changed'}
 Write-Output 'PASS user can delete TailsideModSetup after closing app; plugins/config remain.'
 $passed=$true
} finally {
 $env:MODSETUP_SANDBOX_ROOT=$oldSandbox
 if(Get-Process Tailside,TailsideModSetup -ErrorAction SilentlyContinue){throw "Test process still active; originals retained in $backup"}
 foreach($name in $paths){$src=[IO.Path]::GetFullPath((Join-Path $GameRoot $name));$dst=[IO.Path]::GetFullPath((Join-Path $backup ('tested-'+$name)));if(!$src.StartsWith($GameRoot+'\',[StringComparison]::OrdinalIgnoreCase) -or !$dst.StartsWith($backup+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe restoration path'};if(Test-Path -LiteralPath $src){Move-Item -LiteralPath $src -Destination $dst}}
 foreach($name in $moved){Move-Item -LiteralPath (Join-Path $backup $name) -Destination (Join-Path $GameRoot $name)}
 if((Snapshot) -ne $before -or (Get-FileHash -LiteralPath "$GameRoot/Tailside.exe").Hash -ne $exeHash){throw 'Original isolated files not restored'}
 [ordered]@{completed=$passed;isolatedFilesRestored=$true;backup=$backup}|ConvertTo-Json|Set-Content -Encoding utf8 "$backup/result.json"
 Write-Output "PASS original isolated files restored; evidence: $backup"
}
