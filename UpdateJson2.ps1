$trPath = 'E:\Kodlarim\Yeni\src\Integration.TradeXpress.Domain.Shared\Localization\TradeXpress\tr.json'
$trJson = Get-Content $trPath -Raw -Encoding UTF8 | ConvertFrom-Json
$trJson.texts | Add-Member -MemberType NoteProperty -Name 'Enum:ProcessPaymentType:Consignment:UI' -Value 'EMANET' -Force
$trJson.texts | Add-Member -MemberType NoteProperty -Name 'Enum:ProcessPaymentType:Reservation:UI' -Value 'REZERVE' -Force
$trJson | ConvertTo-Json -Depth 10 | Set-Content $trPath -Encoding UTF8
