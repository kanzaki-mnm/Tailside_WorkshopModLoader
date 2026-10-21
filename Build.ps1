param(
    [string]$GameRoot,
    [string]$LoaderCore,
    [string]$ManagedDir,
    [switch]$WithTools,
    [switch]$RunChecks,
    [string]$BepInExZip
)
$ErrorActionPreference='Stop'
$references=@{}
$properties=@()
foreach($name in @('GameRoot','LoaderCore','ManagedDir')) {
    $value=Get-Variable -Name $name -ValueOnly
    if($value){$references[$name]=[IO.Path]::GetFullPath($value);$properties+="-p:${name}=$($references[$name])"}
}
if($BepInExZip){$BepInExZip=[IO.Path]::GetFullPath($BepInExZip)}
if($WithTools -and (!$BepInExZip -or !(Test-Path -LiteralPath $BepInExZip -PathType Leaf))){
    throw 'WithTools requires -BepInExZip pointing to the official BepInEx 5.4.23.5 Windows x64 archive.'
}
Push-Location $PSScriptRoot
try {
    & ./TailsideWorkshopModLoader/Build.ps1 @references
    if($RunChecks){
        & ./tests/Loader/Run-Checks.ps1 @references
        # Compile all isolated-runtime test helpers without launching the game.
        foreach($project in @('tests/Loader/SmokeProbe/SmokeProbe.csproj','tests/Loader/TestDriver/TestDriver.csproj','tests/SandboxGuard/SandboxGuard.csproj')){
            dotnet build $project -c Release -m:1 --configfile TailsideWorkshopModLoader/NuGet.Config @properties
            if($LASTEXITCODE){throw "Test helper build failed: $project"}
        }
        & ./tests/Loader/Collision/Build-Fixtures.ps1 @references
    }
    if($WithTools){
        & ./TailsideModSetup/Build.ps1 -DevelopmentPreview -BepInExZip $BepInExZip
        if($RunChecks){
            foreach($project in @('tests/Setup/Checks/Checks.csproj','tests/Setup/EngineChecks/Checks.csproj','tests/Setup/Integration/Integration.csproj','tests/Setup/UiChecks/UiChecks.csproj','tests/Setup/LifecycleChecks/LifecycleChecks.csproj')){
                dotnet build $project -c Release -m:1 --configfile TailsideModSetup/NuGet.Config
                if($LASTEXITCODE){throw "Tool check project build failed: $project"}
            }
            dotnet run --project tests/Setup/Checks/Checks.csproj -c Release --no-build --no-restore
            if($LASTEXITCODE){throw 'Unified core checks failed'}
            dotnet run --project tests/Setup/EngineChecks/Checks.csproj -c Release --no-build --no-restore -- (Join-Path $PSScriptRoot 'TailsideModSetup/Assets/runtime.zip')
            if($LASTEXITCODE){throw 'Setup checks failed'}
        }
    }
    Write-Output 'Build complete. No game process was launched and no game installation was changed.'
} finally {Pop-Location}
