[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [string]$DownloadUrl = '',
    [string]$FeedUrl = '',
    [string]$Notes = 'Новая версия LauncherPlaySpace.'
)
$ErrorActionPreference='Stop'
$workspace=Split-Path -Parent $PSScriptRoot
$releaseFolder=Join-Path $workspace "artifacts/releases/$Version"
if(Test-Path -LiteralPath $releaseFolder){throw "Папка выпуска уже существует: $releaseFolder. Используйте новый номер версии."}
foreach($link in @($DownloadUrl,$FeedUrl)) {
    if($link -and (([uri]$link).Scheme -ne 'https' -or ([uri]$link).UserInfo)){throw 'Для ссылок необходим HTTPS без встроенных паролей.'}
}
$appFolder=Join-Path $releaseFolder 'app'
New-Item -ItemType Directory -Path $appFolder -Force | Out-Null
$project=Join-Path $workspace 'GameLauncherGUI/GameLauncherGUI.csproj'
& dotnet publish $project -c Release -r win-x64 --self-contained true '-p:PublishSingleFile=true' '-p:DebugType=None' '-p:DebugSymbols=false' "-p:Version=$Version" -o $appFolder
if($LASTEXITCODE -ne 0){throw 'Сборка выпуска завершилась ошибкой.'}
foreach($notice in @('LICENSE','THIRD_PARTY_NOTICES.md')){Copy-Item -LiteralPath (Join-Path $workspace $notice) -Destination $appFolder}
if($FeedUrl){@{feedUrl=$FeedUrl}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $appFolder 'update-source.json') -Encoding UTF8}
$archive=Join-Path $releaseFolder "LauncherPlaySpace-$Version-win-x64.zip"
Compress-Archive -Path (Join-Path $appFolder '*') -DestinationPath $archive
if($DownloadUrl){@{version=$Version;downloadUrl=$DownloadUrl;notes=$Notes}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $releaseFolder 'latest.json') -Encoding UTF8}
Write-Output "Готов архив: $archive"
if(-not $DownloadUrl){Write-Output 'После загрузки архива опубликуйте latest.json с версией и полученной HTTPS-ссылкой на архив.'}
