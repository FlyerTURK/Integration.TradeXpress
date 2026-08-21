using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace Integration.TradeXpress.TrendyolShipments;

/// <summary>
/// Trendyol kargo firması — <b>HOST-GLOBAL</b> referans (<c>IMultiTenant</c> DEĞİL; TenantId yok → tüm tenant'lar
/// aynı listeyi paylaşır). N11'deki <c>N11ShipmentCompany</c>'nin Trendyol karşılığı.
///
/// <para><b>SYNC DEĞİL, SEED (2026-08-04 keşfi):</b> N11 firmalarını canlı servisten çekiyoruz
/// (<c>GetShipmentCompanies</c>), Trendyol'da öyle bir uç YOKTUR — resmî doküman listeyi <b>statik tablo</b>
/// olarak yayınlar ("Trendyol Kargo Şirketleri Listesi (getProviders)"; Id + Kod + Ad + Vergi No). Envanterimizde
/// bu satır uç sanılıyordu; doğrulanmış bir URL taşımıyordu. Liste ~10 satır ve seyrek değişir → seed doğru yol.
/// Trendyol yeni firma yayınlarsa seeder güncellenir (idempotent: mevcut satır ADI/KODU tazelenir, silinmez).</para>
///
/// <para><b>Maliyet BURADA DEĞİL:</b> kargo anlaşması satıcıya özeldir — bir satıcının pazarlıklı fiyatını
/// host-global satıra yazmak onu tüm tenant'lara sızdırırdı. Tahmini maliyet company-owned tarafta durur
/// (N11'de <c>N11ShipmentTemplate.EstimatedCost</c>'un yaptığı gibi).</para>
///
/// <para><b>Core'u BİLMEZ</b> — <c>N11ShipmentCompany</c> ile aynı katman yönü: uzak durumu yansıtan bu entity
/// tenant/company dünyasından habersizdir, bağ sahipli tarafta kurulur.</para>
/// </summary>
public class TrendyolCargoProvider : FullAuditedAggregateRoot<Guid>
{
    #region Constructors

    protected TrendyolCargoProvider()
    {
    }

    public TrendyolCargoProvider(string externalId, string code, string name, string? taxNumber)
    {
        ExternalId = StringFieldGuard.EnsureRequiredText(
            externalId, nameof(ExternalId), 1, TrendyolShipmentConsts.ExternalIdMaxLength);
        SetCode(code);
        SetName(name);
        SetTaxNumber(taxNumber);
        IsActive = true;
    }

    #endregion

    #region Properties

    /// <summary>Trendyol kargo firması id'si (ör. "7"). Ürün body'sindeki <c>cargoCompanyId</c> bununla eşleşir.
    /// Global benzersiz. Set-once — kimlik.</summary>
    public virtual string ExternalId { get; protected set; } = string.Empty;

    /// <summary>Trendyol kısa kodu (ör. "ARASMP").</summary>
    public virtual string Code { get; protected set; } = string.Empty;

    public virtual string Name { get; protected set; } = string.Empty;

    /// <summary>Vergi numarası (resmî listeden). Bugün kullanılmıyor; kargo faturası/cari eşleşmesi yazıldığında
    /// firmayı muhasebe tarafında tanımanın en güvenilir anahtarı olacak.</summary>
    public virtual string? TaxNumber { get; protected set; }

    /// <summary>Firma hâlâ kullanılabilir mi. Trendyol listeden çıkarırsa satır SİLİNMEZ → pasifleşir
    /// (<c>N11ShipmentCompany</c> senkronundaki aynı gerekçe: geçmiş referanslar ve kullanıcının kurduğu
    /// bağlar yaşamalı).</summary>
    public virtual bool IsActive { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetCode(string code)
    {
        Code = StringFieldGuard.EnsureRequiredText(code, nameof(Code), 1, TrendyolShipmentConsts.CodeMaxLength);
    }

    public virtual void SetName(string name)
    {
        Name = StringFieldGuard.EnsureRequiredText(name, nameof(Name), 1, TrendyolShipmentConsts.NameMaxLength);
    }

    public virtual void SetTaxNumber(string? taxNumber)
    {
        TaxNumber = StringFieldGuard.EnsureOptionalText(
            taxNumber, nameof(TaxNumber), 1, TrendyolShipmentConsts.TaxNumberMaxLength);
    }

    public virtual void SetActive(bool value)
    {
        IsActive = value;
    }

    public override string ToString()
    {
        return Code;
    }

    #endregion
}
