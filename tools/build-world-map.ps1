# Dünya haritasını (assets\Dunya-Haritasi.jpg) oyunun kendi minimap karolarından üretir.
# Kaynak: github.com/JellyBitz/xSROMap (MIT) içindeki assets/img/silkroad/minimap/6 karoları.
# Karo: 256x256, 4x4 bölge (1 bölge = 192 oyun birimi). Sonuç harita %75 ölçekte,
# koordinat eşlemesi kesindir: piksel = koordinat / 4 + ofset (X için 3408, Y için 1020, Y ters).
# Bu sabitler src\WorldMap.cs içindeki AX..BC değerleridir.
param([string]$TilesDir=(Join-Path $env:TEMP 'xsro-tiles6'))
$ErrorActionPreference='Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)
Add-Type -AssemblyName System.Drawing
if(!(Test-Path $TilesDir)){New-Item -ItemType Directory -Force $TilesDir|Out-Null}
$headers=@{'User-Agent'='NewEra-map-build'}
$tree=Invoke-RestMethod 'https://api.github.com/repos/JellyBitz/xSROMap/git/trees/master?recursive=1' -Headers $headers
foreach($f in ($tree.tree | ? {$_.type -eq 'blob' -and $_.path -match '^assets/img/silkroad/minimap/6/\d+x\d+\.jpg$'})){
 $out=Join-Path $TilesDir (Split-Path $f.path -Leaf)
 if(!(Test-Path $out)){Invoke-WebRequest ('https://raw.githubusercontent.com/JellyBitz/xSROMap/master/'+$f.path) -OutFile $out -UseBasicParsing}
}
$tx0=16;$tx1=44;$ty0=17;$ty1=29;$ts=256
$W=($tx1-$tx0+1)*$ts;$H=($ty1-$ty0+1)*$ts
$full=New-Object Drawing.Bitmap $W,$H,([Drawing.Imaging.PixelFormat]::Format24bppRgb)
$g=[Drawing.Graphics]::FromImage($full);$g.Clear([Drawing.Color]::FromArgb(20,30,45))
for($ty=$ty0;$ty -le $ty1;$ty++){for($tx=$tx0;$tx -le $tx1;$tx++){$f=Join-Path $TilesDir "${tx}x${ty}.jpg";if(Test-Path $f){$im=[Drawing.Image]::FromFile($f);$g.DrawImage($im,($tx-$tx0)*$ts,($ty1-$ty)*$ts,$ts,$ts);$im.Dispose()}}}
$out=New-Object Drawing.Bitmap 5568,2100,([Drawing.Imaging.PixelFormat]::Format24bppRgb)
$g2=[Drawing.Graphics]::FromImage($out);$g2.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic;$g2.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::Half
$g2.DrawImage($full,(New-Object Drawing.Rectangle 0,0,5568,2100),(New-Object Drawing.Rectangle 0,240,$W,2800),[Drawing.GraphicsUnit]::Pixel)
$enc=[Drawing.Imaging.ImageCodecInfo]::GetImageEncoders()|?{$_.MimeType -eq 'image/jpeg'}
$ep=New-Object Drawing.Imaging.EncoderParameters 1;$ep.Param[0]=New-Object Drawing.Imaging.EncoderParameter([Drawing.Imaging.Encoder]::Quality,[long]90)
$target=Join-Path (Get-Location) 'assets\Dunya-Haritasi.jpg'
$out.Save($target,$enc,$ep)
Write-Output "Hazır: $target"