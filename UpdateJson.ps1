$trPath = 'E:\Kodlarim\Yeni\src\Integration.TradeXpress.Domain.Shared\Localization\TradeXpress\tr.json'
$trJson = Get-Content $trPath -Raw -Encoding UTF8 | ConvertFrom-Json
$trJson.Texts | Add-Member -MemberType NoteProperty -Name 'Enum:ProcessDirectionType:Inbound:UI' -Value 'GIRIS' -Force
$trJson.Texts | Add-Member -MemberType NoteProperty -Name 'Enum:ProcessDirectionType:Outbound:UI' -Value 'CIKIS' -Force
$trJson.Texts | Add-Member -MemberType NoteProperty -Name 'Enum:ProcessPaymentType:Normal:UI' -Value 'NORMAL' -Force
$trJson.Texts | Add-Member -MemberType NoteProperty -Name 'Enum:ProcessPaymentType:WithCash:UI' -Value 'PESIN' -Force
$trJson.Texts | Add-Member -MemberType NoteProperty -Name 'Enum:ProcessPaymentType:WithCurrency:UI' -Value 'BEDELLI' -Force
$trJson.Texts | Add-Member -MemberType NoteProperty -Name 'Enum:ProcessPaymentType:Return:UI' -Value 'IADE' -Force
$trJson.Texts | Add-Member -MemberType NoteProperty -Name 'Enum:ProcessPaymentType:Consignment:UI' -Value 'EMANET' -Force
$trJson.Texts | Add-Member -MemberType NoteProperty -Name 'Enum:ProcessPaymentType:Reservation:UI' -Value 'REZERVASYON' -Force
$trJson | ConvertTo-Json -Depth 10 | Set-Content $trPath -Encoding UTF8

$enPath = 'E:\Kodlarim\Yeni\src\Integration.TradeXpress.Domain.Shared\Localization\TradeXpress\en.json'
$enJson = Get-Content $enPath -Raw -Encoding UTF8 | ConvertFrom-Json
$enJson.Texts | Add-Member -MemberType NoteProperty -Name 'Enum:ProcessDirectionType:Inbound:UI' -Value 'INBOUND' -Force
$enJson.Texts | Add-Member -MemberType NoteProperty -Name 'Enum:ProcessDirectionType:Outbound:UI' -Value 'OUTBOUND' -Force
$enJson.Texts | Add-Member -MemberType NoteProperty -Name 'Enum:ProcessPaymentType:Normal:UI' -Value 'NORMAL' -Force
$enJson.Texts | Add-Member -MemberType NoteProperty -Name 'Enum:ProcessPaymentType:WithCash:UI' -Value 'PREPAID' -Force
$enJson.Texts | Add-Member -MemberType NoteProperty -Name 'Enum:ProcessPaymentType:WithCurrency:UI' -Value 'WITH CURRENCY' -Force
$enJson.Texts | Add-Member -MemberType NoteProperty -Name 'Enum:ProcessPaymentType:Return:UI' -Value 'RETURN' -Force
$enJson.Texts | Add-Member -MemberType NoteProperty -Name 'Enum:ProcessPaymentType:Consignment:UI' -Value 'CONSIGNMENT' -Force
$enJson.Texts | Add-Member -MemberType NoteProperty -Name 'Enum:ProcessPaymentType:Reservation:UI' -Value 'RESERVATION' -Force
$enJson | ConvertTo-Json -Depth 10 | Set-Content $enPath -Encoding UTF8
