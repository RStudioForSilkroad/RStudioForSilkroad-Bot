# 2) Yeni sürümü derler, imzalar ve GitHub'a yüklenecek dosyaları release\ klasörüne koyar.
# Örnek: .\tools\2-yayin-hazirla.ps1 -Version 3.4.0.0 -Repo kullanici/depo
param(
 [Parameter(Mandatory=$true)][string]$Version,
 [Parameter(Mandatory=$true)][string]$Repo,
 [string]$KeyDir=(Join-Path $env:USERPROFILE 'NewEraKeys')
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
Set-Location $root
if($Version -notmatch '^\d+\.\d+\.\d+\.\d+$'){throw 'Sürüm 4 parçalı olmalı, örn 3.4.0.0'}
if($Repo -notmatch '^[\w.-]+/[\w.-]+$'){throw 'Repo "kullanici/depo" biçiminde olmalı'}
$priv=Join-Path $KeyDir 'updates-private-key.xml'
if(!(Test-Path $priv)){throw 'Önce 1-anahtar-uret.ps1 çalıştır.'}
# sürüm numarası
$ai=Join-Path $root 'src\AssemblyInfo.cs'
$text=[IO.File]::ReadAllText($ai)
$text=[regex]::Replace($text,'AssemblyVersion\("[^"]+"\)',"AssemblyVersion(`"$Version`")")
$text=[regex]::Replace($text,'AssemblyFileVersion\("[^"]+"\)',"AssemblyFileVersion(`"$Version`")")
[IO.File]::WriteAllText($ai,$text,(New-Object Text.UTF8Encoding($true)))
# derleme
$rel=Join-Path $root 'release'; New-Item -ItemType Directory -Force $rel | Out-Null
$exe=Join-Path $rel 'SilkroadRStudio.exe'
& (Join-Path $root 'tools\build.ps1') -Out $exe | Select-Object -Last 1
if(!(Test-Path $exe)){throw 'Derleme başarısız.'}
# manifest
$tag="v$Version"
$url="https://github.com/$Repo/releases/download/$tag/SilkroadRStudio.exe"
$hash=(Get-FileHash $exe -Algorithm SHA256).Hash.ToUpperInvariant()
$size=(Get-Item $exe).Length
$product='SilkroadRStudio'
$canon=[Text.Encoding]::UTF8.GetBytes("$product`n$Version`n$url`n$hash`n$size`n")
$rsa=New-Object Security.Cryptography.RSACryptoServiceProvider
$rsa.FromXmlString([IO.File]::ReadAllText($priv))
$sig=[Convert]::ToBase64String($rsa.SignData($canon,'SHA256'))
# genel anahtarla doğrula
$pub=New-Object Security.Cryptography.RSACryptoServiceProvider
$pub.FromXmlString([IO.File]::ReadAllText((Join-Path $root 'data\updates-public-key.xml')))
if(!$pub.VerifyData($canon,'SHA256',[Convert]::FromBase64String($sig))){throw 'İmza, projedeki genel anahtarla eşleşmiyor. 1-anahtar-uret.ps1 sonrası yeniden derlendi mi?'}
$manifest=[ordered]@{Product=$product;Version=$Version;Url=$url;Sha256=$hash;Size=$size;Signature=$sig}
[IO.File]::WriteAllText((Join-Path $rel 'manifest.json'),($manifest|ConvertTo-Json),(New-Object Text.UTF8Encoding($false)))
Write-Output ""
Write-Output "Hazır: $rel"
Write-Output "GitHub'da yeni Release oluştur: etiket (tag) = $tag"
Write-Output "Şu iki dosyayı Release'e yükle: SilkroadRStudio.exe ve manifest.json"
Write-Output "Uygulamadaki güncelleme adresi: https://github.com/$Repo/releases/latest/download/manifest.json"