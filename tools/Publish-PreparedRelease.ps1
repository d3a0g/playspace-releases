[CmdletBinding()]
param([Parameter(Mandatory=$true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version)
$ErrorActionPreference='Stop'
$workspace=Split-Path -Parent $PSScriptRoot
$gh=Join-Path $workspace '.tools/github-cli-2.103.3/bin/gh.exe'
$folder=Join-Path $workspace "artifacts/releases/$Version"
$repositories=@('d3a0g/LauncherPlaySpace','d3a0g/playspace-releases')
function CheckedGh([string[]]$Arguments){
    $answer=& $gh @Arguments
    if($LASTEXITCODE -ne 0){throw "GitHub command failed: $($Arguments[0])"}
    return $answer
}
$installer=Join-Path $folder "LauncherPlaySpace-$Version-Setup.exe"
$archive=Join-Path $folder "LauncherPlaySpace-$Version-win-x64.zip"
$manifest=Join-Path $folder 'latest.json'
$checksums=Join-Path $folder 'SHA256SUMS.txt'
$notes=Join-Path $workspace "docs/releases/$Version.md"
foreach($file in @($installer,$archive,$manifest,$notes)){if(-not(Test-Path -LiteralPath $file -PathType Leaf)){throw "Missing prepared file: $file"}}
$feed=Get-Content -LiteralPath $manifest -Raw|ConvertFrom-Json
$installerHash=(Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
$archiveHash=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
if($feed.version -ne $Version -or $feed.installerSha256 -ne $installerHash){throw 'Prepared manifest does not match installer'}
@("$installerHash  $(Split-Path -Leaf $installer)","$archiveHash  $(Split-Path -Leaf $archive)")|Set-Content -LiteralPath $checksums -Encoding ASCII
foreach($repository in $repositories){
    $releases=CheckedGh @('api',"repos/$repository/releases?per_page=100")|ConvertFrom-Json
    if($releases|Where-Object {$_.tag_name -eq "v$Version"}){throw "Release already exists in $repository"}
}
$sourceCommit=''
foreach($repository in @('d3a0g/LauncherPlaySpace','d3a0g/playspace-releases','d3a0g/LauncherPlaySpace-dev')){
    $arguments=@{Repository=$repository;Publish=$true;CommitMessage="Release LauncherPlaySpace ${Version}: in-app silent updates and desktop library";GitHubCliPath=$gh}
    if($repository.EndsWith('-dev')){$arguments.PrivateDevelopment=$true}
    & (Join-Path $PSScriptRoot 'Publish-Source.ps1') @arguments
    if(-not $?){throw "Source publication failed: $repository"}
    if($repository -eq $repositories[0]){$sourceCommit=(Get-Content -LiteralPath (Join-Path $workspace 'artifacts/source-publication/commit.txt') -Raw).Trim()}
}
$assets=@($installer,$archive,$manifest,$checksums)
foreach($repository in $repositories){
    $arguments=@('release','create',"v$Version",'--repo',$repository,'--draft','--title',"LauncherPlaySpace $Version",'--notes-file',$notes)
    if($repository -eq $repositories[0]){$arguments+=@('--target',$sourceCommit)}
    CheckedGh ($arguments+$assets)
    $verification=Join-Path $folder ('verify-'+$repository.Split('/')[1])
    New-Item -ItemType Directory -Force -Path $verification|Out-Null
    CheckedGh @('release','download',"v$Version",'--repo',$repository,'--dir',$verification,'--clobber')
    foreach($asset in $assets){
        $remote=Join-Path $verification (Split-Path -Leaf $asset)
        if((Get-FileHash -LiteralPath $remote -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash){throw "Uploaded asset differs: $remote"}
    }
}
# Switch the live channel only after both drafts have all four verified assets.
foreach($repository in $repositories){CheckedGh @('release','edit',"v$Version",'--repo',$repository,'--draft=false','--latest')}
CheckedGh @('repo','edit',$repositories[0],'--description',"LauncherPlaySpace ${Version}: Windows game library, Playnite-inspired UI, mods, Steam stats and in-app updates.")
CheckedGh @('repo','edit',$repositories[1],'--description',"LauncherPlaySpace ${Version}: Windows x64 installer, portable ZIP and verified in-app update channel.")
CheckedGh @('repo','edit','d3a0g/LauncherPlaySpace-dev','--description',"Private development of LauncherPlaySpace $Version. Public sources: d3a0g/LauncherPlaySpace.")
Write-Output "Published and verified LauncherPlaySpace $Version in both release repositories; all three source trees updated."
