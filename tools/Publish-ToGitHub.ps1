[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')][string]$Repository='d3a0g/playspace-releases',
    [string]$Notes='',
    [string]$NotesFile=''
)
$ErrorActionPreference='Stop'
$workspace=Split-Path -Parent $PSScriptRoot
$Notes=& (Join-Path $PSScriptRoot 'Get-ReleaseNotes.ps1') -Version $Version -Notes $Notes -NotesFile $NotesFile
$ghCommand=Get-Command gh -ErrorAction SilentlyContinue
if($ghCommand){$gh=$ghCommand.Source}else{$gh=Join-Path $workspace '.tools/github-cli-2.103.3/bin/gh.exe'}
if(-not(Test-Path -LiteralPath $gh)){throw 'GitHub CLI не найден.'}
& $gh api user --jq .login
if($LASTEXITCODE -ne 0){throw 'Сначала выполните вход в GitHub CLI.'}
$oldErrorPreference=$ErrorActionPreference
$ErrorActionPreference='Continue'
$existing=& $gh release view "v$Version" --repo $Repository --json tagName 2>$null
$releaseExists=$LASTEXITCODE -eq 0
$ErrorActionPreference=$oldErrorPreference
if($releaseExists){throw "Выпуск v$Version уже существует. Используйте новую версию."}
$feed="https://github.com/$Repository/releases/latest/download/latest.json"
$download="https://github.com/$Repository/releases/download/v$Version/LauncherPlaySpace-$Version-win-x64.zip"
& (Join-Path $PSScriptRoot 'Publish-Release.ps1') -Version $Version -FeedUrl $feed -DownloadUrl $download -Notes $Notes
$folder=Join-Path $workspace "artifacts/releases/$Version"
$archive=Join-Path $folder "LauncherPlaySpace-$Version-win-x64.zip"
$manifest=Join-Path $folder 'latest.json'
$checksums=Join-Path $folder 'SHA256SUMS.txt'
$notesFile=Join-Path $folder 'release-notes.md'
$hash=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  LauncherPlaySpace-$Version-win-x64.zip"|Set-Content -LiteralPath $checksums -Encoding ASCII
[IO.File]::WriteAllText($notesFile,$Notes)
& $gh release create "v$Version" $archive $manifest $checksums --repo $Repository --draft --title "LauncherPlaySpace $Version" --notes-file $notesFile
if($LASTEXITCODE -ne 0){throw 'Загрузка не завершена. Выпуск остаётся черновиком; предыдущий канал обновления не менялся.'}
& $gh release edit "v$Version" --repo $Repository --draft=false --latest
if($LASTEXITCODE -ne 0){throw 'Файлы загружены, но публикация черновика не завершена.'}
Write-Output "Опубликовано: https://github.com/$Repository/releases/tag/v$Version"
Write-Output "Канал обновлений: $feed"
