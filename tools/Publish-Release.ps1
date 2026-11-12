[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [string]$DownloadUrl = '',
    [string]$FeedUrl = '',
    [string]$Notes = '',
    [string]$NotesFile = '',
    [string]$Compiler = ''
)
$ErrorActionPreference='Stop'
$workspace=Split-Path -Parent $PSScriptRoot
$Notes=& (Join-Path $PSScriptRoot 'Get-ReleaseNotes.ps1') -Version $Version -Notes $Notes -NotesFile $NotesFile
$releaseFolder=Join-Path $workspace "artifacts/releases/$Version"
if(-not $Compiler){$Compiler=Join-Path $workspace '.tools/InnoSetup/ISCC.exe'}
if(-not(Test-Path -LiteralPath $Compiler)){throw 'Для сборки установщика нужен Inno Setup ISCC.exe. Укажите -Compiler.'}
if(Test-Path -LiteralPath $releaseFolder){throw "Папка выпуска уже существует: $releaseFolder. Используйте новый номер версии."}
foreach($link in @($DownloadUrl,$FeedUrl)) {
    if($link -and (([uri]$link).Scheme -ne 'https' -or ([uri]$link).UserInfo)){throw 'Для ссылок необходим HTTPS без встроенных паролей.'}
}
if($DownloadUrl -and ([uri]$DownloadUrl).AbsolutePath -match '\.zip$'){throw 'Укажите ссылку на установщик, а не ZIP.'}
$appFolder=Join-Path $releaseFolder 'app'
New-Item -ItemType Directory -Path $appFolder -Force | Out-Null
$project=Join-Path $workspace 'GameLauncherGUI/GameLauncherGUI.csproj'
& dotnet publish $project -c Release -r win-x64 --self-contained true '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' '-p:DebugType=None' '-p:DebugSymbols=false' "-p:Version=$Version" -o $appFolder
if($LASTEXITCODE -ne 0){throw 'Сборка выпуска завершилась ошибкой.'}
foreach($notice in @('LICENSE','THIRD_PARTY_NOTICES.md')){Copy-Item -LiteralPath (Join-Path $workspace $notice) -Destination $appFolder}
Copy-Item -LiteralPath (Join-Path $workspace 'distribution/START-HERE.txt') -Destination $appFolder
if($FeedUrl){@{feedUrl=$FeedUrl}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $appFolder 'update-source.json') -Encoding UTF8}
$archive=Join-Path $releaseFolder "LauncherPlaySpace-$Version-win-x64.zip"
Compress-Archive -Path (Join-Path $appFolder '*') -DestinationPath $archive
$installerName="LauncherPlaySpace-$Version-Setup"
& $Compiler ('/DPayloadDir='+$appFolder) ('/DBuildOutput='+$releaseFolder) ('/DInstallerName='+$installerName) '/DReleaseBuild' (Join-Path $workspace 'installer/LauncherPlaySpace.iss')
if($LASTEXITCODE -ne 0){throw 'Сборка установщика завершилась ошибкой.'}
$installer=Join-Path $releaseFolder ($installerName+'.exe')
if(-not(Test-Path -LiteralPath $installer)){throw 'Файл установщика не создан.'}
if($DownloadUrl){@{version=$Version;downloadUrl=$DownloadUrl;installerUrl=$DownloadUrl;installerSha256=(Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant();notes=$Notes}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $releaseFolder 'latest.json') -Encoding UTF8}
Write-Output "Готов архив: $archive"
Write-Output "Готов установщик: $installer"
if(-not $DownloadUrl){Write-Output 'После загрузки установщика опубликуйте latest.json с версией и HTTPS-ссылкой на EXE установщика.'}
