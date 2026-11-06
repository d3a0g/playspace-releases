[CmdletBinding()]
param([string]$ImagePath='',[string]$ConfigPath='')
$ErrorActionPreference='Stop'
$workspace=Split-Path -Parent $PSScriptRoot
if(-not $ImagePath){$ImagePath=Join-Path $workspace 'PlaySpace.SupportBot/Assets/support-avatar.png'}
if(-not $ConfigPath){$ConfigPath=Join-Path $workspace '.secrets/support-bot.local.json'}
$http=$null
try{
    Add-Type -AssemblyName System.Drawing
    Add-Type -AssemblyName System.Net.Http
    $configuration=[IO.File]::ReadAllText($ConfigPath)|ConvertFrom-Json
    $handler=New-Object System.Net.Http.HttpClientHandler
    if($configuration.proxyUrl){$handler.Proxy=New-Object System.Net.WebProxy($configuration.proxyUrl);$handler.UseProxy=$true}
    $http=New-Object System.Net.Http.HttpClient($handler)
    $http.Timeout=[TimeSpan]::FromSeconds(60)
    $baseUrl='https://api.telegram.org/bot'+$configuration.token+'/'
    function Call([string]$method,$content){
        $response=$http.PostAsync($baseUrl+$method,$content).GetAwaiter().GetResult()
        try{
            if(-not $response.IsSuccessStatusCode){throw 'Telegram request failed.'}
            $result=$response.Content.ReadAsStringAsync().GetAwaiter().GetResult()|ConvertFrom-Json
            if(-not $result.ok){throw 'Telegram rejected request.'}
            return $result.result
        }finally{$response.Dispose()}
    }
    $empty=New-Object System.Net.Http.StringContent('{}',[Text.Encoding]::UTF8,'application/json')
    try{$me=Call 'getMe' $empty}finally{$empty.Dispose()}
    if($me.username -ne 'PlaySpaceSupport_bot'){throw 'Unexpected bot account.'}
    # Telegram requires JPEG. Re-encode the generated image without cropping or resizing.
    $image=[Drawing.Image]::FromFile((Resolve-Path -LiteralPath $ImagePath).Path)
    $bytes=New-Object IO.MemoryStream
    try{$image.Save($bytes,[Drawing.Imaging.ImageFormat]::Jpeg);$photoBytes=$bytes.ToArray()}finally{$image.Dispose();$bytes.Dispose()}
    $multipart=New-Object Net.Http.MultipartFormDataContent
    try{
        $photo=New-Object Net.Http.ByteArrayContent(,$photoBytes)
        $photo.Headers.ContentType=New-Object Net.Http.Headers.MediaTypeHeaderValue('image/jpeg')
        $multipart.Add($photo,'avatar','support-avatar.jpg')
        $multipart.Add((New-Object Net.Http.StringContent('{"type":"static","photo":"attach://avatar"}')),'photo')
        $null=Call 'setMyProfilePhoto' $multipart
    }finally{$multipart.Dispose()}
    Write-Output 'Profile photo installed for @PlaySpaceSupport_bot. No chat messages sent.'
}catch{
    Write-Output 'Avatar update failed. Check bot configuration, network, and Telegram Bot API availability. Credentials were not printed.'
    exit 1
}finally{if($http){$http.Dispose()}}
