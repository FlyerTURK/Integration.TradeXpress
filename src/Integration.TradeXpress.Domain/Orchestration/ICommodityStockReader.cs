using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.TradeXpress.Vouchers;

namespace Integration.TradeXpress.Orchestration;

/// <summary>
/// Emtia/varyant KULLANILABİLİR stoğu okuma soyutlaması (ADR: mock-first test stratejisi — 2026-07-25 Hakan:
/// "tüm sistemler önce mock data ile test edilsin").
/// <para>Gerçek implementasyon ailenin STOK RAPORUNU çağırır (Metal → <c>IMetalReportAppService</c>,
/// Good → <c>IGoodReportAppService</c>; Available = Net − RezerveÇıkış). ICurrentCompany ZORUNLU — şirket
/// bağlamsız BOŞ döner. Test implementasyonu sahte sözlüktür — DB'siz, deterministik.</para>
/// <para><b>İşaret kuralı BURADA KURULMAZ:</b> giriş/çıkış yönü tek kaynaktan
/// (<c>ProcessDirectionTypeExtensions.IsInflow</c>) türetilmiş hazır net değer okunur — 2026-07-25'te ad-hoc
/// sorguda yön varsayımıyla işaretin ters okunması bu soyutlamanın varlık sebebini kanıtladı.</para>
/// <para><b>Aile ANAHTARIN parçasıdır</b> (2026-08-06 — eski ad <c>IMetalStockReader</c>): <c>CommodityId</c>
/// FK'sız snapshot'tır ve aynı Guid farklı ailede çakışabilir. Aileyi anahtara koymak, Metal 3 gramını
/// Good 3 adedine karıştırma ihtimalini <b>yapısal</b> olarak kapatır — sözleşmeye yazılmış bir uyarı değil.</para>
/// </summary>
public interface ICommodityStockReader
{
    /// <summary>Bir AİLENİN verilen emtiaları için kullanılabilir stok. Varyantsız toplam için
    /// <c>CommodityVariantId=null</c> anahtarı da döner. Negatif net mümkündür (fazla çıkış / fazla
    /// rezervasyon) — tüketici sıfıra kırpar.
    /// <para>Aile başına AYRI çağrı: her ailenin kendi rapor servisi vardır; anahtar aileyi taşıdığından
    /// çağıran sonuçları tek sözlükte güvenle birleştirebilir.</para></summary>
    Task<IReadOnlyDictionary<CommodityStockKey, CommodityAvailability>> GetAvailableAsync(
        ProcessType family, IReadOnlyCollection<Guid> commodityIds);
}

/// <summary>Stok havuzu anahtarı: (aile, emtia, opsiyonel emtia varyantı). <c>CommodityVariantId</c> null =
/// varyantsız takip (o emtianın TOPLAMI).</summary>
public readonly record struct CommodityStockKey(ProcessType Family, Guid CommodityId, Guid? CommodityVariantId);

/// <summary>Bir havuzun kullanılabilir stoğu İKİ BOYUTTA. Aileler farklı boyutta ölçülür ve boyutu
/// kaybetmek 2026-07-25'te bir kez oversell'e yol açtı (gram ihtiyacına adet bölünmüştü) — bu yüzden
/// tek sayıya indirgenmez, tüketici hangi boyutu kullandığını AÇIKÇA seçer.
/// <list type="bullet">
///   <item><b>Amount</b> — Metal/Scrap/Future'da GRAM; Good'da stok-birimi miktarı.</item>
///   <item><b>Quantity</b> — ADET (perakende sayım).</item>
/// </list></summary>
public readonly record struct CommodityAvailability(decimal Amount, decimal Quantity)
{
    /// <summary>Seçilen boyuttaki değer.</summary>
    public decimal In(CommodityStockDimension dimension)
    {
        return dimension == CommodityStockDimension.Quantity ? Quantity : Amount;
    }
}

/// <summary>Stok ölçüm boyutu — reçete ihtiyacının hangi sayıyla kıyaslanacağı.</summary>
public enum CommodityStockDimension : byte
{
    /// <summary>Miktar (Metal/Scrap/Future'da gram; Good'da stok-birimi miktarı).</summary>
    Amount = 0,

    /// <summary>Adet.</summary>
    Quantity = 1,
}
