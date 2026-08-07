using System;
using System.Threading.Tasks;
using Integration.TradeXpress.TrendyolProducts;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.TrendyolCategories;

/// <summary>
/// Komisyon oranı KALITIMININ mekanik ağı (2026-08-06 Hakan kararı: oran yalnız belirgin parent'lara girilir,
/// çocuklar miras alır).
///
/// <para><b>Neden test:</b> bu zincirin bozulma biçimi SESSİZDİR — oran bulunamayınca istisna atılmaz, yalnız
/// fiyat yanlış çıkar. Nitekim özellikten önceki hâl tam buydu: <c>resolvedCommissionRate</c> sabit <c>null</c>
/// geçiyordu, komisyon hiçbir reçeteye girmiyordu ve kimse fark etmemişti. Aşağıdaki dört iddia o sessizliği
/// gürültüye çevirir.</para>
/// </summary>
public abstract class TrendyolCommissionResolverTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly TrendyolCommissionResolver _resolver;
    private readonly IRepository<TrendyolCategory, Guid> _repository;
    private readonly ICurrentTenant _currentTenant;

    protected TrendyolCommissionResolverTests()
    {
        _resolver = GetRequiredService<TrendyolCommissionResolver>();
        _repository = GetRequiredService<IRepository<TrendyolCategory, Guid>>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
    }

    /// <summary>Hakan'ın canlıda çözülemeyen vakası: "Kozmetik &amp; Kişisel Bakım &gt; Kadın Hijyen &gt; İntim
    /// Bakım Ürünü". Ara düğüm ve yaprak oransız; oran YALNIZ kökte. İki hop yukarı yürüyüp kökün oranını
    /// bulmalı.</summary>
    [Fact]
    public async Task Leaf_inherits_rate_from_the_nearest_ancestor_that_declares_one()
    {
        var prefix = await SeedChainAsync(rootRate: 17.50m, midRate: null, leafRate: null);

        var rate = await ResolveAsync($"{prefix}-leaf");

        rate.ShouldBe(17.50m);
    }

    /// <summary>Kalıtım "EN YAKIN dolu ata kazanır" demektir — daha derin bir düğüme oran girilirse kökü EZER.
    /// Kanal/kategori özel oranları bu kuralla girilecek (Hakan: "sonrasında satış kanalına özel komisyonları
    /// belirleriz").</summary>
    [Fact]
    public async Task Nearest_ancestor_wins_over_the_root()
    {
        var prefix = await SeedChainAsync(rootRate: 17.50m, midRate: 25.00m, leafRate: null);

        var rate = await ResolveAsync($"{prefix}-leaf");

        rate.ShouldBe(25.00m);
    }

    /// <summary>Ağaçta hiç oran yoksa ya da kategori bilinmiyorsa yer tutucuya düşer — <b>0 DÖNMEZ</b>. Sıfır
    /// komisyon "komisyon yok" demektir ve fiyatı sessizce ~%20 ucuzlatırdı; yaklaşık bir oran hiç olmamasından
    /// iyidir.</summary>
    [Fact]
    public async Task Unknown_category_falls_back_to_the_placeholder_never_to_zero()
    {
        var rate = await ResolveAsync("bilinmeyen-kategori");

        rate.ShouldBe(TrendyolCommissionDefaults.PlaceholderRate);
        rate.ShouldBeGreaterThan(0m);
    }

    /// <summary>Kategorisiz kanal kaydı (import'ta eşleşmemiş olabilir — <c>CategoryId</c> null) da yer tutucuya
    /// düşer; komisyonsuz fiyatlanmaz.</summary>
    [Fact]
    public async Task Missing_category_falls_back_to_the_placeholder()
    {
        (await ResolveAsync(null)).ShouldBe(TrendyolCommissionDefaults.PlaceholderRate);
        (await ResolveAsync("   ")).ShouldBe(TrendyolCommissionDefaults.PlaceholderRate);
    }

    /// <summary>Bozuk veride kendini üst gösteren bir zincir yürüyüşü SONSUZ döndürmemeli — takılırsa test
    /// donar, yani bu iddia aynı zamanda bir zaman aşımı ağıdır.</summary>
    [Fact]
    public async Task Circular_parent_chain_does_not_hang()
    {
        var prefix = NewPrefix();
        await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(null))
            {
                await _repository.InsertAsync(new TrendyolCategory($"{prefix}-a", $"{prefix}-b", "A", isLeaf: false), autoSave: true);
                await _repository.InsertAsync(new TrendyolCategory($"{prefix}-b", $"{prefix}-a", "B", isLeaf: true), autoSave: true);
            }

            return true;
        });

        (await ResolveAsync($"{prefix}-a")).ShouldBe(TrendyolCommissionDefaults.PlaceholderRate);
    }

    /// <summary>Çözücü AMBIENT UoW'a bağlıdır: <c>GetQueryableAsync</c> kendi UoW'unda DbContext üretir, o UoW
    /// kapanınca <c>ToListAsync</c> DISPOSE EDİLMİŞ context'te koşar. Üretimde çağıran hep bir AppService metodudur
    /// (ABP metodu UoW ile sarar); testte sarmayı biz yaparız. Aynı tuzak <c>RecipeCommodityIndex</c>'te de
    /// yaşandı — çözüm testi gevşetmek değil, üretimdeki bağlamı taklit etmektir.</summary>
    private async Task<decimal> ResolveAsync(string? categoryExternalId)
    {
        return await WithUnitOfWorkAsync(async () => await _resolver.ResolveAsync(categoryExternalId));
    }

    /// <summary>Benzersiz ama KISA id ön-eki: <c>ExternalIdMaxLength</c> 32 karakter, tam Guid ("N", 32 hane)
    /// son-ekle birlikte sınırı aşardı.</summary>
    private static string NewPrefix()
    {
        return $"tc{Guid.NewGuid():N}"[..10];
    }

    /// <summary>kök → ara → yaprak zinciri kurar; her testin kendi id uzayı olsun diye ön-ek benzersizdir
    /// (kategori tablosu HOST-GLOBAL, testler arası paylaşılır).</summary>
    private async Task<string> SeedChainAsync(decimal? rootRate, decimal? midRate, decimal? leafRate)
    {
        var prefix = NewPrefix();
        await WithUnitOfWorkAsync(async () =>
        {
            // Kategori host-global → yazım da host bağlamında (production sync deseniyle aynı).
            using (_currentTenant.Change(null))
            {
                await InsertAsync($"{prefix}-root", null, "Kozmetik & Kişisel Bakım", isLeaf: false, rootRate);
                await InsertAsync($"{prefix}-mid", $"{prefix}-root", "Kadın Hijyen", isLeaf: false, midRate);
                await InsertAsync($"{prefix}-leaf", $"{prefix}-mid", "İntim Bakım Ürünü", isLeaf: true, leafRate);
            }

            return true;
        });

        return prefix;
    }

    private async Task InsertAsync(string externalId, string? parentExternalId, string name, bool isLeaf, decimal? rate)
    {
        var category = new TrendyolCategory(externalId, parentExternalId, name, isLeaf);
        category.SetCommissionRate(rate);
        await _repository.InsertAsync(category, autoSave: true);
    }
}
