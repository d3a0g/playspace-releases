[CmdletBinding()]
param([string]$Folder='artifacts/ui-review')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$root=[IO.Path]::GetFullPath($Folder)
foreach($pattern in @('*-100.png','*-120-compact.png')){
    $files=@(Get-ChildItem -LiteralPath $root -Filter $pattern -File | Sort-Object Name)
    for($start=0;$start -lt $files.Count;$start+=12){
        $bitmap=New-Object Drawing.Bitmap(1280,900)
        $graphics=[Drawing.Graphics]::FromImage($bitmap)
        $font=New-Object Drawing.Font('Segoe UI',10)
        try{
            $graphics.Clear([Drawing.Color]::FromArgb(8,12,20))
            $graphics.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            for($slot=0;$slot -lt 12 -and ($start+$slot) -lt $files.Count;$slot++){
                $file=$files[$start+$slot];$x=($slot%4)*320;$y=[Math]::Floor($slot/4)*300
                $graphics.DrawString($file.BaseName,$font,[Drawing.Brushes]::White,[single]($x+6),[single]($y+5))
                $image=[Drawing.Image]::FromFile($file.FullName)
                try{$ratio=[Math]::Min(308.0/$image.Width,268.0/$image.Height);$w=[int]($image.Width*$ratio);$h=[int]($image.Height*$ratio);$graphics.DrawImage($image,[Drawing.Rectangle]::new([int]($x+(320-$w)/2),[int]($y+28),$w,$h))}finally{$image.Dispose()}
            }
            $kind=if($pattern -eq '*-100.png'){'normal'}else{'compact120'}
            $bitmap.Save((Join-Path $root ('contact-'+$kind+'-'+[int]($start/12)+'.png')),[Drawing.Imaging.ImageFormat]::Png)
        }finally{$graphics.Dispose();$font.Dispose();$bitmap.Dispose()}
    }
}
Write-Output 'Created contact sheets from actual UI captures.'
