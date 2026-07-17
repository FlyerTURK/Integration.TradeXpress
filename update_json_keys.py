import json
import codecs

def update_keys(path):
    with codecs.open(path, 'r', 'utf-8-sig') as f:
        data = json.load(f)
    texts = data.get('texts', data.get('Texts', {}))
    
    replacements = {
        'Enum:ProcessDirectionType:Inbound:UI': 'UI:ProcessDirectionType:Inbound',
        'Enum:ProcessDirectionType:Outbound:UI': 'UI:ProcessDirectionType:Outbound',
        'Enum:ProcessPaymentType:Normal:UI': 'UI:ProcessPaymentType:Normal',
        'Enum:ProcessPaymentType:WithCash:UI': 'UI:ProcessPaymentType:WithCash',
        'Enum:ProcessPaymentType:WithCurrency:UI': 'UI:ProcessPaymentType:WithCurrency',
        'Enum:ProcessPaymentType:Return:UI': 'UI:ProcessPaymentType:Return',
        'Enum:ProcessPaymentType:Consignment:UI': 'UI:ProcessPaymentType:Consignment',
        'Enum:ProcessPaymentType:Reservation:UI': 'UI:ProcessPaymentType:Reservation'
    }
    
    for old_k, new_k in replacements.items():
        if old_k in texts:
            texts[new_k] = texts.pop(old_k)
            
    with codecs.open(path, 'w', 'utf-8-sig') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
    print(f"Updated JSON {path}")

update_keys(r'E:\Kodlarim\Yeni\src\Integration.TradeXpress.Domain.Shared\Localization\TradeXpress\tr.json')
update_keys(r'E:\Kodlarim\Yeni\src\Integration.TradeXpress.Domain.Shared\Localization\TradeXpress\en.json')
