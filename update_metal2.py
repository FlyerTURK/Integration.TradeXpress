import json
import codecs

def add_key(path, key, value):
    with codecs.open(path, 'r', 'utf-8-sig') as f:
        data = json.load(f)
    texts = data.get('texts', data.get('Texts', {}))
    texts[key] = value
    with codecs.open(path, 'w', 'utf-8-sig') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)

add_key(r'E:\Kodlarim\Yeni\src\Integration.TradeXpress.Domain.Shared\Localization\TradeXpress\tr.json', 'CommodityId:MetalPanel', 'MADEN')
add_key(r'E:\Kodlarim\Yeni\src\Integration.TradeXpress.Domain.Shared\Localization\TradeXpress\en.json', 'CommodityId:MetalPanel', 'METAL')
