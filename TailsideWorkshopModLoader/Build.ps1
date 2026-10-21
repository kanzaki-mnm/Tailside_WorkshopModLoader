param([string]$GameRoot, [string]$LoaderCore, [string]$ManagedDir)
$ErrorActionPreference = 'Stop'
$properties = @()
foreach ($name in @('GameRoot','LoaderCore','ManagedDir')) {
    $value = Get-Variable -Name $name -ValueOnly
    if ($value) { $properties += "-p:${name}=$([IO.Path]::GetFullPath($value))" }
}
dotnet build (Join-Path $PSScriptRoot 'TailsideWorkshopModLoader.csproj') -c Release --configfile (Join-Path $PSScriptRoot 'NuGet.Config') @properties
if ($LASTEXITCODE) { throw 'Loader build failed.' }
$package = Join-Path $PSScriptRoot 'release/BepInEx/patchers'
New-Item -ItemType Directory -Path $package -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin/Release/netstandard2.1/TailsideWorkshopModLoader.dll') -Destination $package -Force
Write-Output ('Built ' + (Join-Path $package 'TailsideWorkshopModLoader.dll'))
