param([string]$Video, [string]$OutDir, [string]$TimeList)
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
New-Item -ItemType Directory -Force $OutDir | Out-Null
$player = New-Object System.Windows.Media.MediaPlayer
$player.ScrubbingEnabled = $true
$player.Open([Uri]::new($Video))
function Pump([int]$ms) {
    $end = [DateTime]::Now.AddMilliseconds($ms)
    while ([DateTime]::Now -lt $end) {
        [System.Windows.Threading.Dispatcher]::CurrentDispatcher.Invoke([Action]{}, [System.Windows.Threading.DispatcherPriority]::Background)
        Start-Sleep -Milliseconds 20
    }
}
Pump 1500
$w = $player.NaturalVideoWidth; $h = $player.NaturalVideoHeight
"size ${w}x${h} duration " + $player.NaturalDuration
if ($w -le 0) { throw 'video did not open' }
$player.Play(); $player.Pause()
foreach ($t in ($TimeList -split "," | ForEach-Object { [double]::Parse($_, [Globalization.CultureInfo]::InvariantCulture) })) {
    $player.Position = [TimeSpan]::FromSeconds($t)
    Pump 700
    $dv = New-Object System.Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    $dc.DrawVideo($player, [System.Windows.Rect]::new(0, 0, $w, $h))
    $dc.Close()
    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($w, $h, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($dv)
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $path = Join-Path $OutDir ('f' + $t.ToString('00.0', [Globalization.CultureInfo]::InvariantCulture) + '.png')
    $fs = [IO.File]::Create($path); $enc.Save($fs); $fs.Close()
    $path
}
$player.Close()
