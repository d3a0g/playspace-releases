[CmdletBinding()]
param([string]$Repository='d3a0g/LauncherPlaySpace',[switch]$Publish,[switch]$PrivateDevelopment,[string]$CommitMessage='Release LauncherPlaySpace 1.0.5: redesigned UI, graphics and benchmark catalog',[string]$SnapshotDirectory='',[string]$GitHubCliPath='')
$ErrorActionPreference='Stop'
$workspace=Split-Path -Parent $PSScriptRoot
$utf8=New-Object System.Text.UTF8Encoding($false)
$stage=Join-Path $workspace 'artifacts/source-publication'
New-Item -ItemType Directory -Force -Path $stage|Out-Null
$ghCommand=Get-Command gh -ErrorAction SilentlyContinue
if($GitHubCliPath){$gh=$GitHubCliPath}elseif($ghCommand){$gh=$ghCommand.Source}else{$gh=Join-Path $workspace '.tools/github-cli-2.103.3/bin/gh.exe'}
function Request([string]$endpoint,$payload){
    $requestFile=Join-Path $stage 'request.json'
    [IO.File]::WriteAllText($requestFile,($payload|ConvertTo-Json -Depth 25 -Compress),$utf8)
    $answer=& $gh api --method POST $endpoint --input $requestFile
    if($LASTEXITCODE -ne 0){throw "GitHub API failed: $endpoint"}
    return ($answer|ConvertFrom-Json)
}
$files=@()
foreach($directory in @('GameLauncherGUI','GameLauncherGUI.Tests','PlaySpace.SupportBot','tools','docs','installer')){
    $files+=Get-ChildItem -LiteralPath (Join-Path $workspace $directory) -File -Recurse|Where-Object {
        $_.FullName -notmatch '[\\/](bin|obj|signpath)[\\/]' -and $_.Name -notin @('Restore-UiBackup.ps1','PROJECT-CONTEXT.md') -and $_.Extension -in @('.cs','.csproj','.md','.ps1','.ico','.png','.json','.iss','.txt')
    }
}
foreach($name in @('README.md','LICENSE','THIRD_PARTY_NOTICES.md','CONTRIBUTING.md','SECURITY.md','.gitignore','distribution/README.md','distribution/START-HERE.txt')){$files+=Get-Item -LiteralPath (Join-Path $workspace $name)}
if($PrivateDevelopment){
    foreach($name in @('DEVELOPMENT.md','SIGNPATH.md','CODE_SIGNING_POLICY.md','docs/signpath/application-email.txt')){$files+=Get-Item -LiteralPath (Join-Path $workspace $name)}
}
$files=@($files|Sort-Object FullName -Unique)
$nodes=@()
foreach($file in $files){
    $relative=$file.FullName.Substring($workspace.Length+1).Replace('\','/')
    if($relative -match '(^|/)(\.secrets|\.tools|bin|obj|artifacts)/|local\.json$|games\.json$'){throw "Excluded file selected: $relative"}
    if($file.Extension -in @('.ico','.png') -or $relative -in @('LICENSE','GameLauncherGUI/Assets/ThirdParty/NVIDIA-DLSS-LICENSE.txt')){$nodes+=@{path=$relative;mode='100644';type='blob';binary=$true;file=$file.FullName};continue}
    $content=[IO.File]::ReadAllText($file.FullName)
    if($PrivateDevelopment -and $relative -eq 'README.md'){
        $content="# LauncherPlaySpace - private development`n`nThis private repository contains unreleased working sources. It does not publish application updates. See [DEVELOPMENT.md](DEVELOPMENT.md) for collaboration instructions.`n`nPublic project: https://github.com/d3a0g/LauncherPlaySpace`n`n---`n`n"+$content
    }
    if($content -match '(github_pat_[A-Za-z0-9_]{20,}|gh[pousr]_[A-Za-z0-9]{30,}|\b\d{7,}:[A-Za-z0-9_-]{25,})'){throw "Possible secret in $relative"}
    $nodes+=@{path=$relative;mode='100644';type='blob';content=$content}
}
[IO.File]::WriteAllText((Join-Path $stage 'manifest.txt'),(($nodes|ForEach-Object{$_.path}) -join "`n"),$utf8)
Write-Output "Validated $($nodes.Count) public source/documentation/image files; private files excluded."
if($SnapshotDirectory){
    $snapshot=[IO.Path]::GetFullPath($SnapshotDirectory)
    if(Test-Path -LiteralPath $snapshot){throw 'Snapshot destination must be new.'}
    foreach($node in $nodes){$target=Join-Path $snapshot $node.path;New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target)|Out-Null;if($node.binary){Copy-Item -LiteralPath $node.file -Destination $target}else{[IO.File]::WriteAllText($target,$node.content,$utf8)}}
    Write-Output "Frozen source snapshot: $snapshot"
}
if(-not $Publish){return}
if($PrivateDevelopment){
    $metadata=& $gh api "repos/$Repository"
    if($LASTEXITCODE -ne 0 -or -not ($metadata|ConvertFrom-Json).private){throw 'Development sources may only be uploaded to a verified private repository'}
}
$head=& $gh api "repos/$Repository/git/ref/heads/main" --jq .object.sha
if($LASTEXITCODE -ne 0){throw 'Cannot read branch head'}
$baseTree=& $gh api "repos/$Repository/git/commits/$head" --jq .tree.sha
if($LASTEXITCODE -ne 0){throw 'Cannot read branch tree'}
$treeNodes=@()
$remoteFiles=& $gh api "repos/$Repository/git/trees/$baseTree`?recursive=1"
if($LASTEXITCODE -ne 0){throw 'Cannot inspect existing source paths'}
$existing=$remoteFiles|ConvertFrom-Json
if($existing.truncated){throw 'Remote source inventory is truncated'}
$selected=@{};foreach($node in $nodes){$selected[$node.path]=$true}
foreach($entry in $existing.tree){
    if($entry.type -eq 'blob' -and $entry.path -match '^(GameLauncherGUI/|GameLauncherGUI.Tests/|PlaySpace.SupportBot/|tools/|installer/)' -and -not $selected.ContainsKey($entry.path)){
        $treeNodes+=@{path=$entry.path;mode='100644';type='blob';sha=$null}
    }
}
foreach($node in $nodes){
    if($node.binary){
        $blob=Request "repos/$Repository/git/blobs" @{content=[Convert]::ToBase64String([IO.File]::ReadAllBytes($node.file));encoding='base64'}
        $treeNodes+=@{path=$node.path;mode='100644';type='blob';sha=$blob.sha}
        Write-Output "Uploaded $($node.path)"
    }else{$treeNodes+=$node}
}
$tree=Request "repos/$Repository/git/trees" @{base_tree=$baseTree;tree=$treeNodes}
$commit=Request "repos/$Repository/git/commits" @{message=$CommitMessage;tree=$tree.sha;parents=@($head)}
$requestFile=Join-Path $stage 'request.json'
[IO.File]::WriteAllText($requestFile,(@{sha=$commit.sha;force=$false}|ConvertTo-Json -Compress),$utf8)
& $gh api --method PATCH "repos/$Repository/git/refs/heads/main" --input $requestFile --jq .object.sha
if($LASTEXITCODE -ne 0){throw 'Branch update failed'}
[IO.File]::WriteAllText((Join-Path $stage 'commit.txt'),$commit.sha,$utf8)
Write-Output "Source published: https://github.com/$Repository/tree/$($commit.sha)"
