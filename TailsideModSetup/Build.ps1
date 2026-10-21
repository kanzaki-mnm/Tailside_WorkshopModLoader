param([switch]$DevelopmentPreview,[string]$BepInExZip)
if(!$DevelopmentPreview){throw 'Development preview only. Use -DevelopmentPreview for isolated tests; release publication approval is still pending.'}
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$assets=Join-Path $PSScriptRoot 'Assets'
$noticeFiles=@(Get-ChildItem -LiteralPath $assets -File | Where-Object {$_.Name -like '*LICENSE.txt' -or $_.Name -eq 'ThirdPartySources.txt'} | Sort-Object Name)
$zipPath=Join-Path $PSScriptRoot 'Assets/runtime.zip'
$bridge=Join-Path $PSScriptRoot '../TailsideWorkshopModLoader/release/BepInEx/patchers/TailsideWorkshopModLoader.dll'
if (!(Test-Path -LiteralPath $bridge)) { throw 'Build TailsideWorkshopModLoader first.' }
if (!$BepInExZip -or !(Test-Path -LiteralPath $BepInExZip -PathType Leaf)) { throw 'Specify the official BepInEx 5.4.23.5 Windows x64 archive using -BepInExZip.' }
New-Item -ItemType Directory -Path (Split-Path $zipPath) -Force | Out-Null
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath }
$source=[IO.Compression.ZipFile]::OpenRead($BepInExZip)
$output=[IO.Compression.ZipFile]::Open($zipPath,[IO.Compression.ZipArchiveMode]::Create)
$hashes=[ordered]@{}
function Add-PayloadFile([string]$name,[byte[]]$bytes) {
    $entry=$output.CreateEntry($name,[IO.Compression.CompressionLevel]::Optimal)
    $stream=$entry.Open();try{$stream.Write($bytes,0,$bytes.Length)}finally{$stream.Dispose()}
    $hashes[$name]=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
}
try {
    foreach($entry in $source.Entries) {
        if ($entry.FullName -eq 'changelog.txt' -or
            ($entry.FullName.StartsWith('BepInEx/core/',[StringComparison]::Ordinal) -and $entry.FullName.EndsWith('.xml',[StringComparison]::OrdinalIgnoreCase))) { continue }
        $input=$entry.Open();$memory=[IO.MemoryStream]::new()
        try{$input.CopyTo($memory);Add-PayloadFile $entry.FullName $memory.ToArray()}finally{$input.Dispose();$memory.Dispose()}
    }
    Add-PayloadFile 'BepInEx/patchers/TailsideWorkshopModLoader.dll' ([IO.File]::ReadAllBytes($bridge))
    foreach($name in @('en.json','ja.json')) {
        Add-PayloadFile ('BepInEx/TailsideModSetup/languages/'+$name) ([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot ('languages/'+$name))))
    }
    # Keep the installed guide available without publishing a second source document.
    Add-PayloadFile 'BepInEx/TailsideModSetup/languages/README.md' ([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '../README.md')))
    foreach($license in $noticeFiles) {
        Add-PayloadFile ('BepInEx/TailsideModSetup/licenses/'+$license.Name) ([IO.File]::ReadAllBytes($license.FullName))
    }
    $entry=$output.CreateEntry('hashes.json')
    $stream=$entry.Open();$writer=[IO.StreamWriter]::new($stream,[Text.UTF8Encoding]::new($false))
    try{$writer.Write(($hashes|ConvertTo-Json))}finally{$writer.Dispose()}
} finally {$source.Dispose();$output.Dispose()}
dotnet publish (Join-Path $PSScriptRoot 'App/UnifiedApp.csproj') -c Release -m:1 -o (Join-Path $PSScriptRoot 'publish-preview') --configfile (Join-Path $PSScriptRoot 'NuGet.Config')
if($LASTEXITCODE){throw 'Unified build failed'}
$release=Join-Path $PSScriptRoot 'release-preview'
New-Item -ItemType Directory -Force -Path $release | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'publish-preview/TailsideModSetup.exe') -Destination $release
$notices=$noticeFiles | ForEach-Object { $_.Name+"`r`n"+[IO.File]::ReadAllText($_.FullName) }
[IO.File]::WriteAllText((Join-Path $release 'ThirdPartyNotices.txt'),($notices -join "`r`n"))
