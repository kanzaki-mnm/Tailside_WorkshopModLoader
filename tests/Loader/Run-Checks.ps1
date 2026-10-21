param([string]$GameRoot, [string]$LoaderCore, [string]$ManagedDir)
$ErrorActionPreference = 'Stop'
$loaderRoot = $PSScriptRoot
$properties = @()
foreach ($name in @('GameRoot','LoaderCore','ManagedDir')) {
    $value = Get-Variable -Name $name -ValueOnly
    if ($value) { $properties += "-p:${name}=$([IO.Path]::GetFullPath($value))" }
}
& (Join-Path $PSScriptRoot 'Test-ProbeCleanup.ps1')
foreach ($project in @('Checks.csproj', 'Metadata/Metadata.csproj')) {
    dotnet restore (Join-Path $PSScriptRoot $project) --configfile (Join-Path $loaderRoot '../../TailsideWorkshopModLoader/NuGet.Config') @properties
    if ($LASTEXITCODE) { throw "Restore failed: $project" }
    dotnet run --project (Join-Path $PSScriptRoot $project) -c Release --no-restore @properties
    if ($LASTEXITCODE) { throw "Checks failed: $project" }
}
