$iconsToDownload = @{
    'add' = 'plus'
    'delete' = 'trash-2'
    'download' = 'cloud-download'
    'spinner' = 'loader-circle'
    'comments' = 'message-square-more'
    'report' = 'bar-chart-2'
    'save' = 'save'
    'sliders' = 'sliders-horizontal'
    'back' = 'arrow-left'
    'swap' = 'arrow-right-left'
    'address-card' = 'id-card'
    'check' = 'check'
    'refresh' = 'refresh-cw'
    'warning' = 'triangle-alert'
    'percent' = 'percent'
    'history' = 'history'
    'close' = 'x'
    'eye' = 'eye'
    'sign-out' = 'log-out'
    'server' = 'server'
    'bug' = 'bug'
    'copy' = 'copy'
    'chevron-down' = 'chevron-down'
    'lightbulb' = 'lightbulb'
}

$cssContent = ""
$cssBlockStart = "/* ---- Action & System Icons ---- */
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

Add-Content -Path "src\Integration.TradeXpress.Blazor.Client\wwwroot\main.css" -Value $cssBlockStart

Write-Host "Done downloading icons and updating main.css"
