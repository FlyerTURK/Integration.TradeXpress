# DevExpress paketlerini repo-içi nuget-packages/'e vendor'lar (5-10 yıl restore garantisi).
# Yeni bir DevExpress paketi/sürümü referans ettiğinde ÇALIŞTIR → gerekli .nupkg'ler repoya kopyalanır.
# Kaynak: yerel DevExpress kurulumu (offline packages). Hedef: repo\nuget-packages\.
#
# Kullanım: .\vendor-devexpress.ps1   (önce `dotnet restore` ile packages.lock.json'lar güncel olmalı)

param(
    [string]$RepoRoot   = "$PSScriptRoot\..\..",
    [string]$DevExSource = "C:\Program Files\DevExpress 25.2\Components\System\Components\packages",
    [string]$Version    = "25.2.5"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path $RepoRoot
$dst = Join-Path $RepoRoot "nuget-packages"
New-Item -ItemType Directory -Force -Path $dst | Out-Null

# Tüm packages.lock.json'lardan benzersiz DevExpress paket id'lerini topla.
$ids = [System.Collections.Generic.HashSet[string]]::new()
Get-ChildItem $RepoRoot -Recurse -Filter packages.lock.json |
    Where-Object { $_.FullName -notlike '*\.claude\*' } |
    ForEach-Object {
        foreach ($m in [regex]::Matches((Get-Content $_.FullName -Raw), '"(DevExpress[A-Za-z.]+)"')) {
            [void]$ids.Add($m.Groups[1].Value)
        }
    }

$copied = 0; $missing = @()
foreach ($id in $ids) {
    $f = Join-Path $DevExSource "$id.$Version.nupkg"
    if (Test-Path $f) { Copy-Item $f $dst -Force; $copied++ } else { $missing += $id }
}
$sizeMB = "{0:N1}" -f (((Get-ChildItem $dst -Filter *.nupkg).Length | Measure-Object -Sum).Sum / 1MB)
Write-Host "Vendored: $copied paket ($sizeMB MB) → $dst"
if ($missing) { Write-Warning "Eksik (kurulumda yok): $($missing -join ', ')" }
