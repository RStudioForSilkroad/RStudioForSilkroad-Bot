# 1) Güncelleme imza anahtarını bir kez üretir.
# Özel anahtar KeyDir içinde kalır (pakete/GitHub'a ASLA koyma). Genel anahtar projeye yazılır.
param([string]$KeyDir=(Join-Path $env:USERPROFILE 'NewEraKeys'))
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$priv=Join-Path $KeyDir 'updates-private-key.xml'
if(Test-Path $priv){throw "Anahtar zaten var: $priv  (üzerine yazılmadı)"}
New-Item -ItemType Directory -Force $KeyDir | Out-Null
$rsa=New-Object Security.Cryptography.RSACryptoServiceProvider 3072
[IO.File]::WriteAllText($priv,$rsa.ToXmlString($true))
[IO.File]::WriteAllText((Join-Path $root 'data\updates-public-key.xml'),$rsa.ToXmlString($false))
Write-Output "Özel anahtar: $priv"
Write-Output "YEDEKLE! Kaybedersen mevcut kullanıcılara güncelleme gönderemezsin."
Write-Output "Genel anahtar projeye yazıldı. Şimdi 2-yayin-hazirla.ps1 ile ilk sürümü derle."