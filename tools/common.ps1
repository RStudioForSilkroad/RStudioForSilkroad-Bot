# Shared compiler settings for build.ps1 and run-tests.ps1. Dot-source this file; it only defines functions.

# Embedded resources: source path -> resource name used by the program.
function Get-ResourceMap {
    $map = [ordered]@{
        'assets/Rikimaru.ico'                         = 'Silkroad.ico'
        'assets/Explorer-Harita-Sade.jpg'             = 'explorer-52.jpg'
        'assets/Dunya-Haritasi.jpg'                   = 'world-map.jpg'
        'assets/Dunya-Oyun-Haritasi.jpg'              = 'world-map-game.jpg'
        'assets/NewEra-Kullanim-Kilavuzu.pdf'         = 'manual.pdf'
        'data/monster-names.tsv'                      = 'monster-names.tsv'
        'data/grim-locations.json'                    = 'grim-locations.json'
        'data/town-regions.json'                      = 'town-regions.json'
        'data/level-exp.json'                         = 'level-exp.json'
        'data/updates-public-key.xml'                 = 'updates-public-key.xml'
        'data/road-mesh.bin.gz'                       = 'road-mesh.bin.gz'
        'data/road-surface.bin.gz'                    = 'road-surface.bin.gz'
        'data/learned-defaults.txt'                   = 'learned-defaults.txt'
        'data/transport-network.json'                 = 'transport-network.json'
        'data/return-scrolls.json'                    = 'return-scrolls.json'
        'data/Nanzhang/Nanzhang-NPC-Haritasi.jpg'     = 'nanzhang-map.jpg'
        'data/Nanzhang/Nanzhang-Harita-Verisi.json'   = 'nanzhang-data.json'
    }
    foreach ($n in 1..18) {
        $code = 'D{0:D2}' -f $n
        $map["data/Grim-Haritalari/$code-Solo-Trial/$code-Solo-Trial.json"] = "GrimFloor.$code.SoloTrial.json"
    }
    foreach ($jpg in Get-ChildItem -LiteralPath 'data/grim-embedded' -Filter '*.jpg') {
        $map["data/grim-embedded/$($jpg.Name)"] = "Grim.$($jpg.Name)"
    }
    return $map
}

# Compiles src\*.cs (plus any extra sources) with the .NET Framework C# compiler. Run from the project root.
function Invoke-Csc {
    param(
        [Parameter(Mandatory = $true)][ValidateSet('winexe', 'exe')][string]$Target,
        [Parameter(Mandatory = $true)][string]$Out,
        [Parameter(Mandatory = $true)][string]$Main,
        [string[]]$ExtraSources = @()
    )
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
    New-Item -ItemType Directory -Force (Split-Path -Parent $Out) | Out-Null

    # CS0649: DTO fields that are filled by JavaScriptSerializer, never assigned in code.
    $compilerArgs = @(
        '/nologo', "/target:$Target", '/platform:x86', '/optimize+', '/utf8output', '/nowarn:649',
        "/out:$Out", "/main:$Main",
        '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll',
        '/reference:System.Core.dll', '/reference:System.Web.Extensions.dll',
        '/win32icon:assets/Rikimaru.ico'
    )
    $resources = Get-ResourceMap
    foreach ($path in $resources.Keys) {
        if (-not (Test-Path -LiteralPath $path)) { throw "Missing resource file: $path" }
        $compilerArgs += "/resource:$path,$($resources[$path])"
    }
    $compilerArgs += (Get-ChildItem -LiteralPath 'src' -Filter '*.cs' | Sort-Object Name | ForEach-Object { $_.FullName })
    $compilerArgs += $ExtraSources

    & $compiler @compilerArgs
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
}
