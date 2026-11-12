[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$PlanFile,[switch]$ValidateOnly)
$ErrorActionPreference='Stop'
$plan=Get-Content -LiteralPath $PlanFile -Raw|ConvertFrom-Json
$folder=Split-Path -Parent ([IO.Path]::GetFullPath($PlanFile))
$log=Join-Path $folder 'publication.log'
function CheckedGh([string[]]$Arguments){
    & $plan.GitHubCli @Arguments
    if($LASTEXITCODE -ne 0){throw "GitHub command failed: $($Arguments[0]) $($Arguments[1])"}
}
try{
    Start-Transcript -Path $log -Append|Out-Null
    foreach($file in $plan.Files){
        $path=Join-Path $folder $file.Path
        if(-not(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.SHA256){throw "Frozen publication file changed: $($file.Path)"}
    }
    CheckedGh -Arguments @('api','user','--jq','.login')
    if($ValidateOnly){Write-Output 'Validated frozen files and GitHub authentication. No publication performed.';return}
    if([DateTimeOffset]::UtcNow -lt [DateTimeOffset]::Parse($plan.NotBefore)){throw 'Publication is not allowed before the scheduled time.'}
    if(Test-Path -LiteralPath (Join-Path $folder 'completed.json')){Write-Output 'Already published.';return}
    $source=Join-Path $folder 'source'
    $marker=Join-Path $folder 'source-commit.txt'
    if(-not(Test-Path -LiteralPath $marker)){
        & (Join-Path $source 'tools/Publish-Source.ps1') -Publish -Repository $plan.SourceRepository -GitHubCliPath $plan.GitHubCli -CommitMessage ('Release LauncherPlaySpace '+$plan.Version)
        if(-not $?){throw 'Source publication failed.'}
        Copy-Item -LiteralPath (Join-Path $source 'artifacts/source-publication/commit.txt') -Destination $marker
    }
    $commit=([IO.File]::ReadAllText($marker)).Trim()
    foreach($description in $plan.Descriptions){
        CheckedGh -Arguments @('repo','edit',$description.Repository,'--description',$description.Text)
    }
    foreach($document in $plan.RepositoryDocuments){
        $sha=& $plan.GitHubCli api ('repos/'+$document.Repository+'/contents/'+$document.Path) --jq .sha
        if($LASTEXITCODE -ne 0){throw 'Cannot read repository document revision.'}
        $request=@{message=('Update documentation for LauncherPlaySpace '+$plan.Version);sha=($sha -join '').Trim();content=[Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $folder $document.File)))}
        $requestPath=Join-Path $folder 'repository-document-request.json'
        [IO.File]::WriteAllText($requestPath,($request|ConvertTo-Json -Compress),[Text.UTF8Encoding]::new($false))
        CheckedGh -Arguments @('api','--method','PUT',('repos/'+$document.Repository+'/contents/'+$document.Path),'--input',$requestPath,'--silent')
    }
    $assets=@($plan.Assets|ForEach-Object{Join-Path $folder $_})
    $tag='v'+$plan.Version
    foreach($repository in @($plan.SourceRepository,$plan.UpdateRepository)){
        $ErrorActionPreference='Continue'
        $draft=& $plan.GitHubCli release view $tag --repo $repository --json isDraft 2>$null
        $found=$LASTEXITCODE -eq 0
        $ErrorActionPreference='Stop'
        if(-not $found){
            $arguments=@('release','create',$tag,'--repo',$repository,'--draft','--title',('LauncherPlaySpace '+$plan.Version),'--notes-file',(Join-Path $folder 'release-notes.md'))
            if($repository -eq $plan.SourceRepository){$arguments+=@('--target',$commit)}
            CheckedGh $arguments
            $isDraft=$true
        }else{$isDraft=($draft|ConvertFrom-Json).isDraft}
        if($isDraft){CheckedGh (@('release','upload',$tag,'--repo',$repository,'--clobber')+$assets)}
    }
    # Publish the source release first; switch the live update feed only after all assets exist.
    foreach($repository in @($plan.SourceRepository,$plan.UpdateRepository)){
        CheckedGh -Arguments @('release','edit',$tag,'--repo',$repository,'--draft=false','--latest','--notes-file',(Join-Path $folder 'release-notes.md'))
    }
    @{Version=$plan.Version;Commit=$commit;Published=[DateTimeOffset]::UtcNow.ToString('o')}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $folder 'completed.json') -Encoding UTF8
    Write-Output 'Publication completed in both repositories.'
}catch{Write-Output ('Publication failed: '+$_.Exception.Message);exit 1}
finally{Stop-Transcript -ErrorAction SilentlyContinue|Out-Null}
