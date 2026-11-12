[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$workspace=Split-Path -Parent $PSScriptRoot
$config=Join-Path $workspace '.secrets/support-bot.local.json'
if(-not(Test-Path -LiteralPath $config)){throw 'Нет локальной конфигурации .secrets/support-bot.local.json'}
$pidFile=Join-Path $workspace '.secrets/support-bot.pid'
if(Test-Path -LiteralPath $pidFile){$workerId=[int](Get-Content -LiteralPath $pidFile);$existing=Get-CimInstance Win32_Process -Filter "ProcessId=$workerId" -ErrorAction SilentlyContinue;if($existing -and $existing.CommandLine -like '*PlaySpace.SupportBot.dll*'){Write-Output 'Обработчик поддержки уже запущен.';return}}
$project=Join-Path $workspace 'PlaySpace.SupportBot/PlaySpace.SupportBot.csproj'
& dotnet build $project -c Release --verbosity quiet
if($LASTEXITCODE -ne 0){throw 'Не удалось собрать службу поддержки.'}
$dll=Join-Path $workspace 'PlaySpace.SupportBot/bin/Release/net10.0/PlaySpace.SupportBot.dll'
& dotnet $dll $config --check-connection
if($LASTEXITCODE -ne 0){throw 'Проверка подключения не пройдена. Укажите рабочий HTTP/SOCKS5-прокси в proxyUrl локальной конфигурации.'}
$process=Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList @(('"'+$dll+'"'),('"'+$config+'"')) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $workspace '.secrets/support-bot-output.log') -RedirectStandardError (Join-Path $workspace '.secrets/support-bot-error.log')
$process.Id|Set-Content -LiteralPath $pidFile
Write-Output 'Обработчик поддержки запущен в фоне. Для постоянной работы оставьте ПК и настроенный прокси доступными.'
