namespace Integration.TradeXpress.SalesChannels;

/// <summary>
/// Kanal-tarafı sıra son eki — TEK KURAL, TEK YER (2026-08-07 Hakan bulgusu):
/// <b>ilk sahip ÇIPLAK kodu alır, son ek 2'den başlar; "-1" HİÇBİR YERDE üretilmez.</b>
///
/// <para><b>Neden:</b> öncesinde altı üretici (N11 <c>SellerCode</c> · Trendyol <c>ProductMainId</c> · Etsy
/// <c>SellerSkuBase</c> + üç kanalın varyant SKU kod üreticisi) ilk listelemede bile "-{SequenceNo}" yapıştırıyordu —
/// kodu "1234" olan ürün kanala "1234-1" olarak gidiyordu. Son ekin amacı AYNI ürünün İKİNCİ listelemesini
/// ayırmaktır; tek listeleme (ezici çoğunluk) gürültü taşımamalı. Ürün kodu benzersizleştiricisi de aynı felsefeyle
/// çalışır (taban · "-2" · "-3"…): çıplak kod ile "-2" ayrıştığı için benzersizlik bozulmaz.</para>
///
/// <para><b>Kapsam — yalnız BİZİM ürettiğimiz kodlar:</b> içe aktarılan listelemenin uzak kimliği (stockCode /
/// barcode / sku) pazaryerinde YAŞAR ve donduğu gibi kalır; bu kural o yola hiç dokunmaz. Daha önce "-1" ile
/// dondurulmuş SKU satırları da değişmez (üreticiler yalnız YENİ kayıt/satır için çağrılır).</para>
/// </summary>
public static class ChannelSequenceCode
{
    public static string Compose(string code, int sequenceNo)
    {
        if (sequenceNo <= 1)
        {
            return code;
        }

        return $"{code}-{sequenceNo}";
    }
}
