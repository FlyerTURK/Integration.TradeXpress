import json
import codecs

path = r'E:\Kodlarim\Yeni\src\Integration.TradeXpress.Domain.Shared\Localization\TradeXpress\tr.json'

with codecs.open(path, 'r', 'utf-8') as f:
    data = json.load(f)

texts = data.get('texts', data.get('Texts', {}))

texts['Enum:ProcessDirectionType:Inbound:UI'] = 'GIRIS'
texts['Enum:ProcessDirectionType:Outbound:UI'] = 'CIKIS'
texts['Enum:ProcessPaymentType:Normal:UI'] = 'NORMAL'
texts['Enum:ProcessPaymentType:WithCash:UI'] = 'PESIN'
texts['Enum:ProcessPaymentType:WithCurrency:UI'] = 'BEDELLI'
texts['Enum:ProcessPaymentType:Return:UI'] = 'IADE'
texts['Enum:ProcessPaymentType:Consignment:UI'] = 'EMANET'
texts['Enum:ProcessPaymentType:Reservation:UI'] = 'REZERVE'

with codecs.open(path, 'w', 'utf-8') as f:
    json.dump(data, f, ensure_ascii=False, indent=2)

