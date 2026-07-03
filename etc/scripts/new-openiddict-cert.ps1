# OpenIddict imza/şifreleme sertifikası üretir — mevcut openiddict.pfx 2027-06-11'de doluyor (1 yıl).
# Bu script UZUN ÖMÜRLÜ (varsayılan 10 yıl) yeni bir cert üretir → sessiz auth çöküşünü önler.
#
# ⚠ DİKKAT: Yeni cert eski token'ları GEÇERSİZ kılar → herkes bir kez yeniden login olur (tek seferlik, kabul edilebilir).
# ⚠ Yeni CertificatePassPhrase'i appsettings.secrets.json'a (HttpApi.Host + Blazor) gir — appsettings.json'a DEĞİL.
#
# Kullanım:
#   .\new-openiddict-cert.ps1 -PassPhrase "<YENI-GUID-PAROLA>" -Years 10
# Üretilen openiddict.pfx'i src\...\HttpApi.Host\ ve src\...\Blazor\ altına kopyala (ikisi aynı cert'i kullanır).

param(
    [Parameter(Mandatory=$true)][string]$PassPhrase,
    [int]$Years = 10,
    [string]$OutDir = "$PSScriptRoot\..\.."   # repo kökü
)

$ErrorActionPreference = "Stop"
$notAfter = (Get-Date).AddYears($Years)

# OpenIddict hem imza hem şifreleme için kullanır; KeyUsage geniş bırakılır (DigitalSignature + KeyEncipherment + DataEncipherment).
$cert = New-SelfSignedCertificate `
    -Subject "CN=TradeXpress OpenIddict" `
    -KeyUsage DigitalSignature, KeyEncipherment, DataEncipherment `
    -KeyExportPolicy Exportable `
    -KeyAlgorithm RSA -KeyLength 2048 `
    -NotAfter $notAfter `
    -CertStoreLocation "Cert:\CurrentUser\My"

$pfxPath = Join-Path (Resolve-Path $OutDir) "openiddict.pfx"
$sec = ConvertTo-SecureString -String $PassPhrase -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $sec | Out-Null

# Cert store'daki geçici kaydı temizle (yalnız pfx dosyası kalsın).
Remove-Item "Cert:\CurrentUser\My\$($cert.Thumbprint)" -Force

Write-Host "OK: $pfxPath  (NotAfter: $($notAfter.ToString('yyyy-MM-dd')))"
Write-Host "Sonraki adım: pfx'i HttpApi.Host\ ve Blazor\ altına kopyala + CertificatePassPhrase'i appsettings.secrets.json'a yaz."
Write-Host "NOT: Kestrel TLS cert'i (certs\*.crt) AYRI — o Let's Encrypt/Tailscale, 90 günde bir OTOMATİK yenilenmeli."
