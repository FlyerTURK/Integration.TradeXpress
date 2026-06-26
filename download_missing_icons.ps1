$iconsToDownload = @{
    'address-card' = 'contact'
    'spinner' = 'loader-2'
    'warning' = 'alert-triangle'
    'refresh' = 'rotate-cw'
    'download' = 'download-cloud'
}

foreach ($kv in $iconsToDownload.GetEnumerator()) {
    $name = $kv.Key
    $lucide = $kv.Value
    $url = "https://unpkg.com/lucide-static@0.344.0/icons/$lucide.svg"
    $dest = "src\Integration.TradeXpress.Blazor.Client\wwwroot\images\custom\$name.svg"
    
    Write-Host "Downloading $name.svg..."
    Invoke-WebRequest -Uri $url -OutFile $dest
}
Write-Host "Done downloading missing icons"
