$iconsToDownload = @{
    'edit' = 'pencil'
    'search' = 'search'
    'more' = 'more-horizontal'
    'info' = 'info'
    'check-circle' = 'check-circle-2'
    'eraser' = 'eraser'
    'eye-off' = 'eye-off'
}

$cssBlockStart = "
/* ---- Framework Additions ---- */
"

foreach ($kv in $iconsToDownload.GetEnumerator()) {
    $name = $kv.Key
    $lucide = $kv.Value
    $url = "https://unpkg.com/lucide-static@0.344.0/icons/$lucide.svg"
    $dest = "src\Integration.TradeXpress.Blazor.Client\wwwroot\images\custom\$name.svg"
    
    Write-Host "Downloading $name.svg..."
    Invoke-WebRequest -Uri $url -OutFile $dest
    
    $cssBlockStart += ".custom-icon-$name { background-image: url('/images/custom/$name.svg'); }
"
}

$cssBlockStart += ".custom-2x { width: 32px !important; height: 32px !important; }
"

Add-Content -Path "src\Integration.TradeXpress.Blazor.Client\wwwroot\main.css" -Value $cssBlockStart

Write-Host "Done downloading framework icons and updating main.css"
