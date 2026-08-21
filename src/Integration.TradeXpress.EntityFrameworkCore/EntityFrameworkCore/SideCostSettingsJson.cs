using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>
/// <see cref="SideCostSettings"/> owned VO'sunun <b>TEK JSON kolonu</b> (AppSalesChannels.SideCosts) serileştirimi.
/// NEDEN value-converter (EF native <c>ToJson()</c> DEĞİL): EF Core'un ToJson'ı yalnız <b>TPH</b> kalıtımı destekler;
/// SalesChannel hiyerarşisi <b>TPT</b> (UseTptMappingStrategy) → design-time'da "Only TPH inheritance is supported"
/// hatası. Şema kararı (tek JSON kolon, alt tiplere alan yayılmaz) DEĞİŞMEDEN System.Text.Json dönüştürücüsüyle
/// sağlanır. VO'nun protected ctor/setter'ları contract-modifier'la AÇILIR (domain'e serialization attribute
/// SIZMAZ — encapsulation korunur); guard'lar yazım yolunda (SetSideCosts → VO ctor) zaten çalışmıştır.
///
/// <para><b>Eski şema toleransı (2026-07-10 yeniden şekillendirme):</b> yazım HEP yeni şemadır
/// (<c>{"Items":[...]}</c>); okuma, "Items" alanı OLMAYAN eski sabit-alanlı payload'ı (PackagingCost/CargoCost/
/// InsuredShipping*/DefaultCommissionRate/PerSaleFixedFee/ExtraFeeRate + 4 fiş hedefi) gider satırlarına
/// DÖNÜŞTÜRÜR — kullanıcının test verisi kaybolmaz; ilk kayıtta kolon yeni şemayla yenilenir. Migration YOK
/// (aynı kolon, yeni payload).</para>
/// </summary>
public static class SideCostSettingsJson
{
    private static readonly JsonSerializerOptions Options = BuildOptions();

    public static string? Serialize(SideCostSettings? value)
    {
        return value is null ? null : JsonSerializer.Serialize(value, Options);
    }

    public static SideCostSettings? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        // Yeni şema işareti = "Items" alanı; yoksa eski sabit-alanlı payload'dır → toleranslı dönüşüm.
        if (document.RootElement.TryGetProperty("Items", out _))
        {
            return JsonSerializer.Deserialize<SideCostSettings>(json, Options);
        }

        return ConvertLegacyPayload(json);
    }

    private static JsonSerializerOptions BuildOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(EnableNonPublicMembers);
        return new JsonSerializerOptions { TypeInfoResolver = resolver };
    }

    // Yalnız yan-maliyet VO tipleri için: protected parametresiz ctor + protected setter'ları reflection'la aç.
    private static void EnableNonPublicMembers(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type != typeof(SideCostSettings) && typeInfo.Type != typeof(SideCostItem))
        {
            return;
        }

        typeInfo.CreateObject = () => Activator.CreateInstance(typeInfo.Type, nonPublic: true)!;

        foreach (var property in typeInfo.Properties)
        {
            if (property.Set is not null)
            {
                continue;
            }

            // JSON adı = CLR adı (PascalCase; naming policy yok) → doğrudan property lookup güvenli.
            var setter = typeInfo.Type.GetProperty(property.Name)?.SetMethod;
            if (setter is not null)
            {
                property.Set = (obj, value) => setter.Invoke(obj, new[] { value });
            }
        }
    }

    // ── Eski şema dönüşümü — sabit alanlar → gider satırları (sıra: paketleme → kargo → sigortalı →
    //    kanal-sabit → komisyon → Offsite Ads; eski composer üretim sırasıyla hizalı) ────────────────

    private static SideCostSettings ConvertLegacyPayload(string json)
    {
        var legacy = JsonSerializer.Deserialize<LegacySideCostSettings>(json) ?? new LegacySideCostSettings();
        var items = new List<SideCostItem>();

        // Değer YA DA hizmet bağı girilmiş kalemler taşınır (yalnız tutara bakmak kullanıcının kurduğu
        // hizmet/cari bağını düşürürdü); hiç dokunulmamış kalem satır üretmez.
        if (legacy.PackagingCost is not null || legacy.Packaging?.ServiceId is not null)
        {
            items.Add(BuildLegacyItem(
                SideCostKind.Packaging, SideCostCalcMode.FixedAmount, legacy.PackagingCost ?? 0m,
                legacy.CostCurrencyUnitId, legacy.Packaging, SideCostPostingMode.Expense,
                items.Count, autoRate: false, requiresVariantOptIn: false));
        }

        if (legacy.CargoCost is not null || legacy.Cargo?.ServiceId is not null)
        {
            items.Add(BuildLegacyItem(
                SideCostKind.Cargo, SideCostCalcMode.FixedAmount, legacy.CargoCost ?? 0m,
                legacy.CostCurrencyUnitId, legacy.Cargo, SideCostPostingMode.CounterpartyAccount,
                items.Count, autoRate: false, requiresVariantOptIn: false));
        }

        // Sigortalı gönderim: eski mod 1=Fixed, 2=PercentOfValue (0=None → kalem yok). Varyant opt-in deseni
        // genellemeyle taşınır (RequiresVariantOptIn) — kanal tanımlıysa aktif, varyantta açılmadıkça uygulanmaz.
        if (legacy.InsuredShippingMode is 1 or 2
            && (legacy.InsuredShippingValue is not null || legacy.InsuredShipping?.ServiceId is not null))
        {
            var calcMode = legacy.InsuredShippingMode == 1 ? SideCostCalcMode.FixedAmount : SideCostCalcMode.PercentOfCost;
            items.Add(BuildLegacyItem(
                SideCostKind.InsuredShipping, calcMode, legacy.InsuredShippingValue ?? 0m,
                legacy.CostCurrencyUnitId, legacy.InsuredShipping, SideCostPostingMode.CounterpartyAccount,
                items.Count, autoRate: false, requiresVariantOptIn: true));
        }

        if (legacy.PerSaleFixedFee is not null)
        {
            items.Add(BuildLegacyItem(
                SideCostKind.ChannelFixed, SideCostCalcMode.FixedAmount, legacy.PerSaleFixedFee ?? 0m,
                legacy.CostCurrencyUnitId, legacy.Commission, SideCostPostingMode.CounterpartyAccount,
                items.Count, autoRate: false, requiresVariantOptIn: false));
        }

        // Eski şemada AKTİF GrossUp TOPLAMI guard'ı yoktu (yalnız kalem-başı sınır) — yeni SideCostSettings
        // ctor'unun Σ-guard'ı OKUMA anında fırlamasın diye koşan toplam izlenir; sınırı AŞACAK kalem
        // IsEnabled=false ile taşınır (veri korunur, kayıt açılabilir kalır, kullanıcı formda düzeltir).
        var grossUpTotal = 0m;

        // Komisyon: eski davranış tüm kanallarda "çözülmüş oran ?? kanal varsayılanı" idi → AutoRate=true birebir
        // korur (N11 kategori oranı; Trendyol/Etsy'de çözüm zaten kanal oranına düşer, Value fallback).
        if (legacy.DefaultCommissionRate is not null || legacy.Commission?.ServiceId is not null)
        {
            items.Add(BuildLegacyItem(
                SideCostKind.Commission, SideCostCalcMode.GrossUpPercent, legacy.DefaultCommissionRate ?? 0m,
                costCurrencyUnitId: null, legacy.Commission, SideCostPostingMode.CounterpartyAccount,
                items.Count, autoRate: true, requiresVariantOptIn: false));
            grossUpTotal += legacy.DefaultCommissionRate ?? 0m;
        }

        // Etsy Offsite Ads: eskiden komisyon oranına EKLENEN tek GrossUp'tı; yeni modelde AYRI GrossUp satırı
        // (adı sabit — kullanıcı grid'de değiştirir). GrossUp satırları motor kuralıyla zaten en sonda.
        if (legacy.ExtraFeeRate is > 0m)
        {
            var extraFeeEnabled =
                grossUpTotal + legacy.ExtraFeeRate.Value < ProductRecipeConsts.GrossUpOperandExclusiveMax;
            items.Add(new SideCostItem(
                SideCostKind.Commission, "Offsite Ads", SideCostCalcMode.GrossUpPercent, legacy.ExtraFeeRate.Value,
                currencyUnitId: null, serviceId: legacy.Commission?.ServiceId,
                SideCostPostingMode.CounterpartyAccount,
                legacy.Commission?.AccountId, legacy.Commission?.SubAccountId,
                autoRate: false, isEnabled: extraFeeEnabled, displayOrder: items.Count, requiresVariantOptIn: false));
        }

        return new SideCostSettings(items);
    }

    private static SideCostItem BuildLegacyItem(
        SideCostKind kind,
        SideCostCalcMode calcMode,
        decimal value,
        Guid? costCurrencyUnitId,
        LegacyPostingTarget? target,
        SideCostPostingMode defaultPostingMode,
        int displayOrder,
        bool autoRate,
        bool requiresVariantOptIn)
    {
        // Eski fiş modu 1=CounterpartyAccount, 2=Expense (enum değerleri korunarak taşındı).
        var postingMode = target?.PostingMode is 2 ? SideCostPostingMode.Expense
            : target?.PostingMode is 1 ? SideCostPostingMode.CounterpartyAccount
            : defaultPostingMode;

        return new SideCostItem(
            kind, displayName: null, calcMode, value,
            currencyUnitId: costCurrencyUnitId,
            serviceId: target?.ServiceId,
            postingMode,
            accountId: target?.AccountId,
            subAccountId: target?.AccountId is null ? null : target?.SubAccountId,
            autoRate, isEnabled: true, displayOrder, requiresVariantOptIn);
    }

    /// <summary>Eski sabit-alanlı payload'ın salt-okuma karşılığı — yalnız dönüşüm için (yazılmaz).</summary>
    private sealed class LegacySideCostSettings
    {
        public decimal? PackagingCost { get; set; }
        public decimal? CargoCost { get; set; }
        public int InsuredShippingMode { get; set; }
        public decimal? InsuredShippingValue { get; set; }
        public decimal? DefaultCommissionRate { get; set; }
        public decimal? PerSaleFixedFee { get; set; }
        public decimal? ExtraFeeRate { get; set; }
        public Guid? CostCurrencyUnitId { get; set; }
        public LegacyPostingTarget? Packaging { get; set; }
        public LegacyPostingTarget? Cargo { get; set; }
        public LegacyPostingTarget? InsuredShipping { get; set; }
        public LegacyPostingTarget? Commission { get; set; }
    }

    private sealed class LegacyPostingTarget
    {
        public int PostingMode { get; set; }
        public Guid? ServiceId { get; set; }
        public Guid? AccountId { get; set; }
        public Guid? SubAccountId { get; set; }
    }
}
