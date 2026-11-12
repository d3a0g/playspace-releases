[CmdletBinding()]
param([string]$Payload='artifacts/local-preview/improvements/app')
$ErrorActionPreference='Stop'
$workspace=Split-Path -Parent $PSScriptRoot
$compiler=Join-Path $workspace '.tools/InnoSetup/ISCC.exe'
$payload=Join-Path $workspace $Payload
$smokeRoot=Join-Path $workspace ('artifacts/installer-smoke-'+[Guid]::NewGuid().ToString('N'))
$install=Join-Path $smokeRoot 'installed'
$output=Join-Path $smokeRoot 'setup'
$registry='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\LauncherPlaySpace.LocalSmoke_is1'
if(Test-Path $registry){throw 'A previous smoke installation still exists. Do not overwrite it.'}
if(-not $install.StartsWith($workspace+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid test installation target.'}
New-Item -ItemType Directory -Force -Path $output | Out-Null
& $compiler /DSmokeTest ('/DPayloadDir='+$payload) ('/DBuildOutput='+$output) (Join-Path $workspace 'installer/LauncherPlaySpace.iss') *> (Join-Path $smokeRoot 'compile.log')
if($LASTEXITCODE -ne 0){throw 'Smoke installer compilation failed.'}
$setup=Join-Path $output 'LauncherPlaySpace-Setup-preview.exe'
function Install([string]$executable){
    $process=Start-Process -FilePath $executable -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="'+$install+'"'),'/GROUP="LauncherPlaySpace Local Smoke"','/TASKS=""',('/PLAYSPACEPROGRESS="'+(Join-Path $smokeRoot 'progress.txt')+'"'),('/LOG="'+(Join-Path $smokeRoot 'install.log')+'"')) -WindowStyle Hidden -Wait -PassThru
    return $process.ExitCode
}
try{
    if((Install $setup) -ne 0){throw 'Fresh installation failed.'}
    if([int](Get-Content -LiteralPath (Join-Path $smokeRoot 'progress.txt') -Raw) -lt 100){throw 'Installer did not report completed progress.'}
    $exe=Join-Path $install 'LauncherPlaySpace.exe'
    $expected=(Get-FileHash (Join-Path $payload 'LauncherPlaySpace.exe')).Hash
    if((Get-FileHash $exe).Hash -ne $expected){throw 'Installed executable differs from prepared payload.'}
    $link=Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs/LauncherPlaySpace Local Smoke/LauncherPlaySpace - Safe Mode.lnk'
    if(-not(Test-Path -LiteralPath $link)){throw 'Safe mode shortcut missing.'}
    $shell=New-Object -ComObject WScript.Shell
    try{$shortcut=$shell.CreateShortcut($link);if($shortcut.TargetPath -ne $exe -or $shortcut.Arguments -ne '--safe-mode'){throw 'Shortcut target or arguments invalid.'}}
    finally{if($shortcut){[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)};[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)}
    $marker=Join-Path $install 'unmanaged-user-file.txt';[IO.File]::WriteAllText($marker,'fixture must survive upgrades and uninstall')
    $oldExe=Join-Path $workspace 'artifacts/releases/1.0.4/app/LauncherPlaySpace.exe'
    if(-not(Test-Path -LiteralPath $oldExe)){throw 'Old release fixture required for upgrade verification.'}
    Copy-Item -LiteralPath $oldExe -Destination $exe -Force
    if((Install $setup) -ne 0 -or (Get-FileHash $exe).Hash -ne $expected -or -not(Test-Path -LiteralPath $marker)){throw 'In-place upgrade failed or removed an unrelated file.'}
    $oldPayload=Join-Path $smokeRoot 'old-payload';New-Item -ItemType Directory -Path $oldPayload | Out-Null
    Copy-Item -LiteralPath $oldExe -Destination (Join-Path $oldPayload 'LauncherPlaySpace.exe')
    foreach($notice in @('LICENSE','THIRD_PARTY_NOTICES.md','START-HERE.txt')){Copy-Item -LiteralPath (Join-Path $payload $notice) -Destination $oldPayload}
    $oldOutput=Join-Path $smokeRoot 'old-setup'
    & $compiler /DSmokeTest ('/DPayloadDir='+$oldPayload) ('/DBuildOutput='+$oldOutput) (Join-Path $workspace 'installer/LauncherPlaySpace.iss') *> (Join-Path $smokeRoot 'compile-old.log')
    if($LASTEXITCODE -ne 0){throw 'Older fixture compilation failed.'}
    if((Install (Join-Path $oldOutput 'LauncherPlaySpace-Setup-preview.exe')) -eq 0 -or (Get-FileHash $exe).Hash -ne $expected){throw 'Downgrade was not blocked.'}
    $verify=Start-Process -FilePath $exe -ArgumentList '--verify-package' -WindowStyle Hidden -Wait -PassThru
    if($verify.ExitCode -ne 0){throw 'Installed package verification failed.'}
    'PASS: isolated installer fresh install, safe-mode shortcut, replacement of 1.0.4 EXE, downgrade rejection and installed package verification.'
}finally{
    $uninstaller=Join-Path $install 'unins000.exe'
    if(Test-Path -LiteralPath $uninstaller){$process=Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -WindowStyle Hidden -Wait -PassThru;if($process.ExitCode -ne 0){throw 'Smoke uninstaller failed.'}}
}
if(Test-Path -LiteralPath (Join-Path $install 'LauncherPlaySpace.exe')){throw 'Uninstaller left owned EXE.'}
if(-not(Test-Path -LiteralPath (Join-Path $install 'unmanaged-user-file.txt'))){throw 'Uninstaller removed unrelated file.'}
if(Test-Path $registry){throw 'Smoke uninstall registration was not removed.'}
'PASS: isolated uninstall removed owned application/registration, preserved unrelated file. User application data and normal installation were not modified.'
