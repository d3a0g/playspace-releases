[CmdletBinding()]
param(
    [ValidatePattern('^[a-fA-F0-9]{64}$')][string]$SHA256='f2652e1ed56ce4ef26795a953e054ed685edeb46d387f8f74ca8b24e32397690',
    [string]$KeyFile='',
    [string]$ReportFile='',
    [string]$Proxy='',
    [string]$FileLabel='LauncherPlaySpace-1.0.5-Setup.exe',
    [ValidatePattern('^[A-Za-z0-9_.-]+\.png$')][string]$ImageName='virustotal-installer.png',
    [string]$OutputDirectory='artifacts/virustotal'
)
$ErrorActionPreference='Stop'
$workspace=Split-Path -Parent $PSScriptRoot
$SHA256=$SHA256.ToLowerInvariant()
if(-not [IO.Path]::IsPathRooted($OutputDirectory)){$OutputDirectory=Join-Path $workspace $OutputDirectory}
New-Item -ItemType Directory -Force -Path $OutputDirectory|Out-Null
if($ReportFile){$report=Get-Content -LiteralPath $ReportFile -Raw|ConvertFrom-Json}
else{
    if(-not $KeyFile){$KeyFile=Join-Path $workspace '.secrets/virustotal.local.json'}
    $apiKey=$env:VT_API_KEY
    if(-not $apiKey -and (Test-Path -LiteralPath $KeyFile)){$apiKey=(Get-Content -LiteralPath $KeyFile -Raw|ConvertFrom-Json).apiKey}
    if([string]::IsNullOrWhiteSpace($apiKey)){throw 'Save your VirusTotal API key in .secrets/virustotal.local.json (apiKey). Do not send the key in chat.'}
    [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
    $request=@{Method='Get';Uri=('https://www.virustotal.com/api/v3/files/'+$SHA256);Headers=@{'x-apikey'=$apiKey.Trim()};TimeoutSec=60}
    if($Proxy){$request.Proxy=$Proxy}
    try{$report=Invoke-RestMethod @request}
    catch{throw 'VirusTotal request failed. Check the key, quota, access and network. No file was uploaded.'}
    finally{$apiKey=$null}
}
$a=$report.data.attributes
if($report.data.id -ne $SHA256 -or $a.sha256 -ne $SHA256){throw 'Report hash does not match the requested file.'}
if(-not $a.last_analysis_stats -or -not $a.last_analysis_results -or -not $a.last_analysis_date){throw 'Report does not contain a completed analysis.'}
$engines=@($a.last_analysis_results.PSObject.Properties|ForEach-Object{[pscustomobject]@{Engine=$_.Name;Category=$_.Value.category;Result=$_.Value.result;Updated=$_.Value.engine_update}})
$flagged=@($engines|Where-Object{$_.Category -in @('malicious','suspicious')}|Sort-Object Engine)
$url='https://www.virustotal.com/gui/file/'+$SHA256
$analysis=[DateTimeOffset]::FromUnixTimeSeconds([long]$a.last_analysis_date).UtcDateTime.ToString('yyyy-MM-dd HH:mm UTC')
$summary=[ordered]@{FileName=$FileLabel;SHA256=$SHA256;AnalysisUTC=$analysis;RetrievedUTC=[DateTimeOffset]::UtcNow.ToString('o');Source=$url;Stats=$a.last_analysis_stats;EngineResults=$engines;Notice='API report summary. Detections are shown without omission. Undetected does not guarantee safety.'}
$utf8=[Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'report.raw.json'),($report|ConvertTo-Json -Depth 80),$utf8)
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'report.public.json'),($summary|ConvertTo-Json -Depth 8),$utf8)
Add-Type -AssemblyName System.Drawing
$height=620+44*[Math]::Max(1,$flagged.Count)
$bitmap=New-Object Drawing.Bitmap(1400,$height)
$graphics=[Drawing.Graphics]::FromImage($bitmap)
$fonts=@();$brushes=@()
try{
    $graphics.Clear([Drawing.Color]::FromArgb(17,21,33));$graphics.TextRenderingHint=[Drawing.Text.TextRenderingHint]::ClearTypeGridFit
    $title=New-Object Drawing.Font('Segoe UI',28,[Drawing.FontStyle]::Bold);$normal=New-Object Drawing.Font('Segoe UI',15);$small=New-Object Drawing.Font('Segoe UI',12);$mono=New-Object Drawing.Font('Consolas',13);$fonts=@($title,$normal,$small,$mono)
    $white=New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(232,237,249));$muted=New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(164,179,203));$alert=New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(255,154,139));$fonts=@($title,$normal,$small,$mono);$brushes=@($white,$muted,$alert)
    $graphics.DrawString('LauncherPlaySpace 1.0.5 - VirusTotal',$title,$white,40,30)
    $graphics.DrawString(($FileLabel+' | SHA-256'),$small,$muted,40,95);$graphics.DrawString($SHA256,$mono,$white,40,125)
    $graphics.DrawString(('Analysis: '+$analysis),$normal,$white,40,180)
    $graphics.DrawString(('Retrieved: '+$summary.RetrievedUTC),$small,$muted,40,220)
    $graphics.DrawString(('Flagged engines: '+$flagged.Count+' / '+$engines.Count),$title,$alert,40,265)
    $stats=($a.last_analysis_stats.PSObject.Properties|ForEach-Object{$_.Name+': '+$_.Value}) -join ' | '
    $graphics.DrawString($stats,$small,$muted,(New-Object Drawing.RectangleF(40,325,1320,65)))
    $y=410
    if($flagged.Count -eq 0){$graphics.DrawString('No malicious/suspicious verdicts in this API response.',$normal,$white,40,$y)}
    foreach($engine in $flagged){$graphics.DrawString($engine.Engine,$normal,$white,40,$y);$graphics.DrawString(($engine.Category+' | '+$engine.Result),$normal,$alert,(New-Object Drawing.RectangleF(460,$y,890,42)));$y+=44}
    $y=$height-165
    $graphics.DrawString('Undetected does not guarantee safety. This is not a security certificate.',$normal,$white,40,$y)
    $graphics.DrawString('Summary generated from the API; not an official VirusTotal screenshot.',$small,$muted,40,($y+40))
    $graphics.DrawString($url,$small,$muted,40,($y+78))
    $bitmap.Save((Join-Path $OutputDirectory $ImageName),[Drawing.Imaging.ImageFormat]::Png)
}finally{foreach($item in $fonts+$brushes){$item.Dispose()};$graphics.Dispose();$bitmap.Dispose()}
Write-Output ('Saved API JSON and PNG: '+$OutputDirectory)
Write-Output ('Flagged: '+$flagged.Count+' / '+$engines.Count+'. No upload or rescan was performed.')
