$trPath = 'E:\Kodlarim\Yeni\src\Integration.TradeXpress.Domain.Shared\Localization\TradeXpress\tr.json'
$trJson = Get-Content $trPath -Raw -Encoding UTF8
$trJson = $trJson -replace '("Enum:ProcessDirectionType:Inbound": "GİRİŞ",)', "$1
    "Enum:ProcessDirectionType:Inbound:UI": "GIRIS","
$trJson = $trJson -replace '("Enum:ProcessDirectionType:Outbound": "ÇIKIŞ",)', "$1
    "Enum:ProcessDirectionType:Outbound:UI": "CIKIS","
$trJson = $trJson -replace '("Enum:ProcessPaymentType:Normal": "NORMAL",)', "$1
    "Enum:ProcessPaymentType:Normal:UI": "NORMAL","
$trJson = $trJson -replace '("Enum:ProcessPaymentType:WithCash": "PEŞİN",)', "$1
    "Enum:ProcessPaymentType:WithCash:UI": "PESIN","
$trJson = $trJson -replace '("Enum:ProcessPaymentType:WithCurrency": "Bedelli",)', "$1
    "Enum:ProcessPaymentType:WithCurrency:UI": "BEDELLI","
$trJson = $trJson -replace '("Enum:ProcessPaymentType:Return": "İade",)', "$1
    "Enum:ProcessPaymentType:Return:UI": "IADE","
$trJson = $trJson -replace '("Enum:ProcessPaymentType:Consignment": "Emanet",)', "$1
    "Enum:ProcessPaymentType:Consignment:UI": "EMANET","
$trJson = $trJson -replace '("Enum:ProcessPaymentType:Reservation": "Rezervasyon",)', "$1
    "Enum:ProcessPaymentType:Reservation:UI": "REZERVE","
Set-Content $trPath -Value $trJson -Encoding UTF8
