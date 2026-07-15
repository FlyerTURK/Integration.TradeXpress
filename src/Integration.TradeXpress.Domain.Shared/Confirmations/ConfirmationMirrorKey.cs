using System;
using Integration.TradeXpress.Vouchers;

namespace Integration.TradeXpress.Confirmations;

/// <summary>
/// <b>Ayna anahtarı</b> — bir Teyit bacağının, karşı bacağın AYNASI olup olmadığını belirleyen ölçüt kümesi
/// (spec §3). İki bağımsız yazılmış process satırı ancak bu anahtar birebir tutuyor (ve yön ZIT) ise ayna sayılır.
///
/// <para><b>TAM ayna (2026-07-14 kararı):</b> yalnız fiziksel bacak (emtia/miktar/birim) DEĞİL, <b>karşılık bacağı</b>
/// (<see cref="PayUnitId"/>/<see cref="PayTotal"/>) da anahtara girer. Gerekçe: A "10 gr altın verdim, 1000 USD",
/// B "10 gr altın aldım, 950 USD" derse fiziksel ayna GEÇER ama iki kasanın carileri netleşmez → tam da teyidin
/// kapatmaya çalıştığı <b>sessiz dengesizlik</b> doğar. Değerlemesiz teslimde karşılık bacağı zaten boştur
/// (iki tarafta da 0 → şart kendiliğinden sağlanır); fiyatlı işlemde koruyucudur.</para>
///
/// <para><b>Quantity + Amount İKİSİ de eşleşir.</b> Tipe göre biri 0 olabilir (Nakit: Amount=para/Quantity=0 ·
/// Mamül/Taş: Quantity=adet) — o durumda şart kendiliğinden sağlanır, ayrı dal gerekmez.</para>
///
/// <para><b><see cref="MainUnitId"/> NULL olabilir:</b> her tipin ana bacağı yoktur — Dekont'ta ana bacak
/// bilinçli olarak boştur (Miktar/Tutar 0, değer <see cref="PayTotal"/>'de). Boş ana bacak <c>null</c> ile
/// temsil edilir (<c>Guid.Empty</c> DEĞİL) → iki taraf da null verir, ayna tutar.</para>
///
/// <para>Record value-eşitliği karşılaştırmayı üstlenir: <c>decimal</c> alanlar sayısal eşitlikle kıyaslanır
/// (15.0 == 15.00), scale farkı ayna bozmaz.</para>
/// </summary>
public sealed record ConfirmationMirrorKey(
    ProcessType          Type,
    ProcessDirectionType Direction,
    Guid?                CommodityId,
    Guid?                VariantId,
    decimal              Quantity,
    decimal              Amount,
    Guid?                MainUnitId,
    Guid?                PayUnitId,
    decimal              PayTotal)
{
    /// <summary>Bu bacağın beklenen AYNASI — yön ZIT'a çevrilir, kalan tüm ölçütler aynen korunur.</summary>
    public ConfirmationMirrorKey Mirrored()
    {
        return this with { Direction = OppositeOf(Direction) };
    }

    /// <summary>Verilen bacak bunun aynası mı (tüm ölçütler eşit + yön zıt)?</summary>
    public bool IsMirroredBy(ConfirmationMirrorKey other)
    {
        return Mirrored() == other;
    }

    /// <summary>Yönün AYNI EKSENDEKİ zıttı: Giriş↔Çıkış · Alacak↔Borç · Alış↔Satış.
    /// <para>Eksen konvansiyonu <see cref="ProcessDirectionTypeExtensions"/> ile hizalı: ÇİFT değer = giriş
    /// (Inbound=0/Credit=2/Buy=4), TEK değer = çıkış. Zıt = eksen içinde komşuya geç (çift→+1, tek→−1) →
    /// eksen KORUNUR (Vadeli'nin aynası Giriş değil Satış'tır).</para></summary>
    public static ProcessDirectionType OppositeOf(ProcessDirectionType direction)
    {
        if (direction.IsInflow())
        {
            return (ProcessDirectionType)((int)direction + 1);
        }

        return (ProcessDirectionType)((int)direction - 1);
    }
}
