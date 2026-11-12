[CmdletBinding()]
param([string]$Compiler='', [string]$Output='artifacts/local-preview/improvements')
$ErrorActionPreference='Stop'
$workspace=Split-Path -Parent $PSScriptRoot
if(-not $Compiler){$Compiler=Join-Path $workspace '.tools/InnoSetup/ISCC.exe'}
if(-not(Test-Path -LiteralPath $Compiler)){throw 'Install the official Inno Setup compiler or supply -Compiler with its ISCC.exe path.'}
$destination=[IO.Path]::GetFullPath((Join-Path $workspace $Output))
if(-not $destination.StartsWith($workspace+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Output must be inside the workspace.'}
$app=Join-Path $destination 'app'
New-Item -ItemType Directory -Force -Path $app | Out-Null
& dotnet publish (Join-Path $workspace 'GameLauncherGUI/GameLauncherGUI.csproj') -c Release -r win-x64 --self-contained true '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' '-p:DebugType=None' '-p:DebugSymbols=false' '-p:NuGetAudit=false' --ignore-failed-sources -o $app
if($LASTEXITCODE -ne 0){throw 'Application publish failed.'}
foreach($name in @('LICENSE','THIRD_PARTY_NOTICES.md')){Copy-Item -LiteralPath (Join-Path $workspace $name) -Destination $app}
Copy-Item -LiteralPath (Join-Path $workspace 'distribution/START-HERE.txt') -Destination $app
& $Compiler ('/DPayloadDir='+$app) ('/DBuildOutput='+$destination) (Join-Path $workspace 'installer/LauncherPlaySpace.iss')
if($LASTEXITCODE -ne 0){throw 'Installer compilation failed.'}
$installer=Join-Path $destination 'LauncherPlaySpace-Setup-preview.exe'
$hash=(Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $destination 'SHA256SUMS.txt'),$hash+'  LauncherPlaySpace-Setup-preview.exe'+[Environment]::NewLine)
Write-Output ('Local unsigned preview installer: '+$installer)
Write-Output 'No GitHub upload, release or application installation was performed.'
