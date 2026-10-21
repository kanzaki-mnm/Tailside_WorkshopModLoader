param([string]$GameRoot,[string]$LoaderCore,[string]$ManagedDir)
$ErrorActionPreference='Stop'
$loader=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$properties=@()
foreach($name in @('GameRoot','LoaderCore','ManagedDir')){$value=Get-Variable -Name $name -ValueOnly;if($value){$properties+="-p:${name}=$([IO.Path]::GetFullPath($value))"}}
foreach($variant in @('A','B')) {
 foreach($version in @('1.0.0.0','2.0.0.0')) {
  dotnet build "$PSScriptRoot/Mod/Mod.csproj" -c Release --no-incremental --configfile "$loader/NuGet.Config" "-p:Variant=$variant" "-p:HelperVersion=$version" -o "$PSScriptRoot/fixtures/$variant-$version" @properties
  if($LASTEXITCODE){throw 'Fixture build failed'}
 }
}
dotnet build "$PSScriptRoot/Mod/Mod.csproj" -c Release --no-incremental --configfile "$loader/NuGet.Config" -p:Variant=A -p:HelperVersion=1.0.0.0 -p:ExtraDefine=REVERSE -o "$PSScriptRoot/fixtures/A-reverse" @properties
if($LASTEXITCODE){throw 'Reverse fixture build failed'}
dotnet build "$PSScriptRoot/Injector/Injector.csproj" -c Release --configfile "$loader/NuGet.Config" @properties
if($LASTEXITCODE){throw 'Injector build failed'}

