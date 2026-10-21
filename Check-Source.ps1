$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$files=@(git -C $root -c core.quotepath=false ls-files --cached --others --exclude-standard)
if($LASTEXITCODE){throw 'Run this check inside the source Git repository.'}
if(!$files.Count){throw 'No publication candidate files found.'}
$failures=[Collections.Generic.List[string]]::new()
$forbidden='(^|/)(\.local|bin|obj|validation|fixtures|steamcmd|backups|publish[^/]*|release[^/]*)(/|$)|\.(dll|exe|pdb|zip|sentry|pem|key)$|(^|/)(Build\.local\.props|\.env(\..*)?|item\.vdf|install\.json|tool-install\.json|recovery\.json|manager-language\.json)$'
$secrets='sf_live_[A-Za-z0-9_-]{15,}|gh[pousr]_[A-Za-z0-9_]{20,}|github_pat_[A-Za-z0-9_]{20,}|-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----|Bearer\s+[A-Za-z0-9_-]{20,}'
foreach($relative in $files){
    if($relative -match $forbidden){$failures.Add("Excluded file is a publication candidate: $relative");continue}
    $path=Join-Path $root $relative
    if(!(Test-Path -LiteralPath $path -PathType Leaf)){continue}
    if([IO.Path]::GetExtension($path) -in @('.png','.ico')){continue}
    $text=[IO.File]::ReadAllText($path)
    if($text -match $secrets){$failures.Add("Potential credential in: $relative")}
    if($text -match '[A-Za-z]:[\\/]Users[\\/](?!Public[\\/])[^\\/\s]+'){$failures.Add("Personal absolute path in: $relative")}
}
# Only the root guide is published; original third-party notices stay separate.
foreach($relative in $files){
    if($relative.EndsWith('.md',[StringComparison]::OrdinalIgnoreCase) -and $relative -ne 'README.md'){
        $failures.Add("Local-only documentation is a publication candidate: $relative")
    }
}
$guide=[IO.File]::ReadAllText((Join-Path $root 'README.md'))
$anchors=@([regex]::Matches($guide,'(?m)^#{1,6} (.+)$') | ForEach-Object {
    (($_.Groups[1].Value.Trim().ToLowerInvariant() -replace '[^a-z0-9 _-]','') -replace ' ','-')
})
foreach($match in [regex]::Matches($guide,'\]\(([^)]+)\)')){
    $target=$match.Groups[1].Value
    if($target.StartsWith('#')){
        if($target.Substring(1) -notin $anchors){$failures.Add("Missing README section: $target")}
    } elseif($target -notmatch '^[a-zA-Z][a-zA-Z0-9+.-]*:'){
        $local=$target.Split('#')[0]
        if($local -notin $files){$failures.Add("README links to unpublished file: $target")}
    }
}
if($failures.Count){throw ($failures -join "`n")}
Write-Output "PASS $($files.Count) publication candidate files: no excluded binaries/state, representative credential patterns, or personal absolute paths found."
Write-Output 'This is a source selection check, not a complete credential or binary security audit.'
