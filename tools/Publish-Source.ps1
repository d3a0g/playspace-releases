[CmdletBinding()]
param([string]$Repository='d3a0g/playspace-releases',[switch]$Publish)
$ErrorActionPreference='Stop'
$workspace=Split-Path -Parent $PSScriptRoot
$utf8=New-Object System.Text.UTF8Encoding($false)
$stage=Join-Path $workspace 'artifacts/source-publication'
New-Item -ItemType Directory -Force -Path $stage|Out-Null
$ghCommand=Get-Command gh -ErrorAction SilentlyContinue
if($ghCommand){$gh=$ghCommand.Source}else{$gh=Join-Path $workspace '.tools/github-cli-2.103.3/bin/gh.exe'}
function Request([string]$endpoint,$payload){
    $requestFile=Join-Path $stage 'request.json'
    [IO.File]::WriteAllText($requestFile,($payload|ConvertTo-Json -Depth 25 -Compress),$utf8)
    $answer=& $gh api --method POST $endpoint --input $requestFile
    if($LASTEXITCODE -ne 0){throw "GitHub API failed: $endpoint"}
    return ($answer|ConvertFrom-Json)
}
$files=@()
foreach($directory in @('GameLauncherGUI','GameLauncherGUI.Tests','PlaySpace.SupportBot','tools','docs/screenshots')){
    $files+=Get-ChildItem -LiteralPath (Join-Path $workspace $directory) -File -Recurse|Where-Object {
        $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and $_.Extension -in @('.cs','.csproj','.md','.ps1','.ico','.png','.json')
    }
}
foreach($name in @('README.md','LICENSE','THIRD_PARTY_NOTICES.md','CONTRIBUTING.md','SECURITY.md','.gitignore','distribution/README.md')){$files+=Get-Item -LiteralPath (Join-Path $workspace $name)}
$files=@($files|Sort-Object FullName -Unique)
$secretFile=Join-Path $workspace '.secrets/support-bot.local.json'
$privateToken=''
if(Test-Path -LiteralPath $secretFile){$privateToken=([IO.File]::ReadAllText($secretFile)|ConvertFrom-Json).token}
$nodes=@()
foreach($file in $files){
    $relative=$file.FullName.Substring($workspace.Length+1).Replace('\','/')
    if($relative -match '(^|/)(\.secrets|\.tools|bin|obj|artifacts)/|local\.json$|games\.json$'){throw "Excluded file selected: $relative"}
    if($file.Extension -in @('.ico','.png')){$nodes+=@{path=$relative;mode='100644';type='blob';binary=$true;file=$file.FullName};continue}
    $content=[IO.File]::ReadAllText($file.FullName)
    if(($privateToken -and $content.Contains($privateToken)) -or $content -match '(github_pat_[A-Za-z0-9_]{20,}|gh[pousr]_[A-Za-z0-9]{30,}|\b\d{7,}:[A-Za-z0-9_-]{25,})'){throw "Possible secret in $relative"}
    $nodes+=@{path=$relative;mode='100644';type='blob';content=$content}
}
[IO.File]::WriteAllText((Join-Path $stage 'manifest.txt'),(($nodes|ForEach-Object{$_.path}) -join "`n"),$utf8)
Write-Output "Validated $($nodes.Count) public source/documentation/image files; private files excluded."
if(-not $Publish){return}
$head=& $gh api "repos/$Repository/git/ref/heads/main" --jq .object.sha
if($LASTEXITCODE -ne 0){throw 'Cannot read branch head'}
$baseTree=& $gh api "repos/$Repository/git/commits/$head" --jq .tree.sha
if($LASTEXITCODE -ne 0){throw 'Cannot read branch tree'}
$treeNodes=@()
foreach($node in $nodes){
    if($node.binary){
        $blob=Request "repos/$Repository/git/blobs" @{content=[Convert]::ToBase64String([IO.File]::ReadAllBytes($node.file));encoding='base64'}
        $treeNodes+=@{path=$node.path;mode='100644';type='blob';sha=$blob.sha}
        Write-Output "Uploaded $($node.path)"
    }else{$treeNodes+=$node}
}
$tree=Request "repos/$Repository/git/trees" @{base_tree=$baseTree;tree=$treeNodes}
$commit=Request "repos/$Repository/git/commits" @{message='Open source LauncherPlaySpace 1.0.3 and add interface gallery';tree=$tree.sha;parents=@($head)}
$requestFile=Join-Path $stage 'request.json'
[IO.File]::WriteAllText($requestFile,(@{sha=$commit.sha;force=$false}|ConvertTo-Json -Compress),$utf8)
& $gh api --method PATCH "repos/$Repository/git/refs/heads/main" --input $requestFile --jq .object.sha
if($LASTEXITCODE -ne 0){throw 'Branch update failed'}
[IO.File]::WriteAllText((Join-Path $stage 'commit.txt'),$commit.sha,$utf8)
Write-Output "Source published: https://github.com/$Repository/tree/$($commit.sha)"
