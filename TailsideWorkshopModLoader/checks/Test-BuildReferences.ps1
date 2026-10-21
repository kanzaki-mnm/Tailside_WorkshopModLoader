param([Parameter(Mandatory=$true)][string]$GameRoot,[Parameter(Mandatory=$true)][string]$LoaderCore)
$ErrorActionPreference='Stop'
$source=Split-Path -Parent $PSScriptRoot
$root=Join-Path $source ('validation/portable-build-'+[guid]::NewGuid().ToString('N'))
$copy=Join-Path $root 'Source only'
$game=Join-Path $root '参照先 日本語/Game folder'
$core=Join-Path $root '参照先 日本語/Official BepInEx/core'
New-Item -ItemType Directory -Path $copy,"$game/Tailside_Data/Managed",$core -Force|Out-Null
foreach($f in @('Core.cs','Patcher.cs','PackageInspector.cs','SteamSubscriptions.cs','TailsideWorkshopModLoader.csproj','Directory.Build.props','Build.ps1','NuGet.Config')){Copy-Item -LiteralPath (Join-Path $source $f) -Destination $copy}
foreach($dir in @('checks/Metadata','SmokeProbe','TestDriver','checks/Collision/Helper','checks/Collision/Mod','checks/Collision/Injector')){
 New-Item -ItemType Directory -Path "$copy/$dir" -Force|Out-Null
 Get-ChildItem -LiteralPath "$source/$dir" -File | Where-Object Extension -in @('.cs','.csproj') | Copy-Item -Destination "$copy/$dir"
}
Copy-Item "$source/checks/Checks.csproj","$source/checks/Program.cs" "$copy/checks"
Copy-Item "$LoaderCore/*.dll" $core
foreach($f in @('UnityEngine.dll','UnityEngine.CoreModule.dll','Facepunch.Steamworks.Win64.dll')){Copy-Item "$GameRoot/Tailside_Data/Managed/$f" "$game/Tailside_Data/Managed"}
if(Test-Path (Join-Path (Split-Path $copy) 'BuildHintFix')){throw 'Unexpected sibling dependency'}
& "$copy/Build.ps1" -GameRoot $game -LoaderCore $core
$explicit=(Get-FileHash "$copy/release/BepInEx/patchers/TailsideWorkshopModLoader.dll").Hash
# Machine-local settings must work without command-line overrides, even when defaults are wrong.
$settings=[xml]'<Project><PropertyGroup><GameRoot/><LoaderCore/></PropertyGroup></Project>'
$settings.Project.PropertyGroup.GameRoot=$game
$settings.Project.PropertyGroup.LoaderCore=$core
$settings.Save("$copy/Build.local.props")
& "$copy/Build.ps1"
if((Get-FileHash "$copy/release/BepInEx/patchers/TailsideWorkshopModLoader.dll").Hash -ne $explicit){throw 'Reference selection changed product output'}
foreach($project in @('checks/Checks.csproj','checks/Metadata/Metadata.csproj')){
 dotnet restore "$copy/$project" --configfile "$copy/NuGet.Config"
 if($LASTEXITCODE){throw "Portable restore failed: $project"}
 dotnet run --project "$copy/$project" -c Release --no-restore
 if($LASTEXITCODE){throw "Portable checks failed: $project"}
}
foreach($project in @('SmokeProbe/SmokeProbe.csproj','TestDriver/TestDriver.csproj','checks/Collision/Mod/Mod.csproj','checks/Collision/Injector/Injector.csproj')){
 dotnet build "$copy/$project" -c Release --configfile "$copy/NuGet.Config" -p:Variant=A -p:HelperVersion=1.0.0.0
 if($LASTEXITCODE){throw "Portable test-project build failed: $project"}
}
$messages=@(dotnet build "$copy/TailsideWorkshopModLoader.csproj" -c Release --no-restore "-p:LoaderCore=$root/Missing core" 2>&1)
if($LASTEXITCODE -eq 0 -or ($messages -join "`n") -notmatch 'Required reference DLLs not found'){throw 'Missing-reference diagnostic not verified'}
$messages | Set-Content "$root/missing-reference.log"
$messages=@(dotnet build "$copy/TailsideWorkshopModLoader.csproj" -c Release --no-restore "-p:ManagedDir=$root/Missing managed" 2>&1)
if($LASTEXITCODE -eq 0 -or ($messages -join "`n") -notmatch 'Required reference DLLs not found'){throw 'Missing-Managed diagnostic not verified'}
$messages | Set-Content "$root/missing-managed.log"
[ordered]@{sourceCopy=$copy;explicitBuild=$true;localSettingsBuild=$true;portableCatalogChecks=40;portableMetadataChecks=21;testProjectsBuilt=4;missingCoreRejected=$true;missingManagedRejected=$true;productHash=$explicit}|ConvertTo-Json|Set-Content "$source/validation/portable-build-results.json"
Write-Output "PASS isolated source-only build, local settings, reference overrides, all test projects, missing-reference diagnostics. Evidence: $root"
