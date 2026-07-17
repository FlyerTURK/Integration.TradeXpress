import json
import codecs

def update_key(path, keys_values):
    with codecs.open(path, 'r', 'utf-8-sig') as f:
        data = json.load(f)
    texts = data.get('texts', data.get('Texts', {}))
    for k, v in keys_values.items():
        texts[k] = v
    with codecs.open(path, 'w', 'utf-8-sig') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)

update_key(r'E:\Kodlarim\Yeni\src\Integration.TradeXpress.Domain.Shared\Localization\TradeXpress\tr.json', {
    'PayTotal:MetalPanel:Cash': 'TUTAR',
    'PayTotal:MetalPanel:Labor': 'TOPLAM İŞÇİLİK'
})
update_key(r'E:\Kodlarim\Yeni\src\Integration.TradeXpress.Domain.Shared\Localization\TradeXpress\en.json', {
    'PayTotal:MetalPanel:Cash': 'AMOUNT',
    'PayTotal:MetalPanel:Labor': 'TOTAL LABOR'
})
