[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$workspace=Split-Path -Parent $PSScriptRoot
$tags=@(@('1663','Шутеры'),@('1662','Выживание'),@('1685','Кооп'),@('1664','Головоломки'),@('1667','Хорроры'),@('9','Стратегии'),@('122','Ролевые'),@('21','Приключения'),@('1643','Песочницы'),@('97376','Уютные'),@('1716','Рогалики'),@('1625','Платформеры'),@('699','Гонки'),@('599','Симуляторы'),@('1666','Карточные'),@('1621','Ритм-игры'))
$catalog=@{}
[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
foreach($pair in $tags){
    $url="https://store.steampowered.com/search/results/?start=0&count=50&category1=998&tags=$($pair[0])&infinite=1&l=english&cc=us"
    $response=$null
    for($attempt=0;$attempt -lt 3;$attempt++){try{$response=Invoke-RestMethod -Uri $url -Headers @{'User-Agent'='LauncherPlaySpace/1.0'} -TimeoutSec 30;break}catch{if($attempt -eq 2){Write-Warning "Category unavailable: $($pair[1])"}}}
    if($null -eq $response){continue}
    foreach($row in [regex]::Matches($response.results_html,'(?s)<a\s[^>]*class="search_result_row[^>]*>.*?</a>')){
        $idMatch=[regex]::Match($row.Value,'data-ds-appid="(\d+)"');$nameMatch=[regex]::Match($row.Value,'<span class="title">([^<]+)</span>')
        if(-not $idMatch.Success -or -not $nameMatch.Success){continue}
        if($row.Value -match 'data-ds-descids="[^"]*\b(3|4)\b'){continue}
        $id=$idMatch.Groups[1].Value;$name=[Net.WebUtility]::HtmlDecode($nameMatch.Groups[1].Value)
        if(-not $catalog.ContainsKey($id)){$catalog[$id]=@{Id=$id;Name=$name;Categories=New-Object Collections.Generic.List[string]}}
        if(-not $catalog[$id].Categories.Contains($pair[1])){$catalog[$id].Categories.Add($pair[1])}
    }
}
$result=@($catalog.Values|Sort-Object Name|ForEach-Object {@{Id=$_.Id;Name=$_.Name;Categories=@($_.Categories)}})
$file=Join-Path $workspace 'GameLauncherGUI/Assets/recommendations-extra.json'
[IO.File]::WriteAllText($file,(ConvertTo-Json -InputObject $result -Depth 5))
Write-Output "Saved $($result.Count) unique Steam games."
