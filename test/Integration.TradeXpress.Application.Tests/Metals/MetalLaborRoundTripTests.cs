using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Vouchers;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Xunit;

namespace Integration.TradeXpress.Metals;

/// <summary>
/// MADEN İŞÇİLİK BAYRAKLARI round-trip ağı (2026-08-07 G2 / Ar-Ge bulgusu A-6 + ACIK-ISLER:51 A4).
///
/// <para><b>Yakalanan kusur:</b> <c>MetalAppService</c> varyant işçiliğini kaydederken üç "değiştirilebilir"
/// bayrağını (<c>LaborTypeChange/EntryLaborChange/ExitLaborChange</c>) ve varyant <c>CostUnitId</c>'sini SABİT
/// <c>false/null</c> yazıyordu; okuma yolları da bayrakları hiç taşımıyordu. Alanlar DTO'da kısmen vardı ama
/// ÖLÜYDÜ. Sonuç: canlıdaki 86 seed madenin tamamı bayraklı doğmuştu ve kullanıcının Metal formunda yapacağı
/// İLK Save bunları GERİ DÖNÜŞSÜZ siliyordu — hatasız, uyarısız, yalnız fiş panelinde işçilik kilitli kalarak.</para>
///
/// <para><b>Hata sınıfı:</b> nesneyi tüketicisinin ihtiyaç duyduğu alanların ALT KÜMESİYLE kurmak — derleme
/// geçer, test geçer, ekranda hata çıkmaz; yalnız veri sessizce kaybolur.</para>
/// </summary>
public abstract class MetalLaborRoundTripTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IMetalAppService _metalAppService;
    private readonly IRepository<CurrencyUnit, Guid> _unitRepository;
    private readonly ICurrentCompany _currentCompany;

    private static readonly Guid FixtureCompanyId = Guid.NewGuid();

    protected MetalLaborRoundTripTests()
    {
        _metalAppService = GetRequiredService<IMetalAppService>();
        _unitRepository = GetRequiredService<IRepository<CurrencyUnit, Guid>>();
        _currentCompany = GetRequiredService<ICurrentCompany>();
    }

    /// <summary>Bayraklar + CostUnitId kayıttan sonra GERİ OKUNMALI ve İKİNCİ bir kayıt onları KORUMALI.
    /// İkinci save şart: kusur tam olarak "dokunulmadan yapılan güncelleme veriyi siliyor" biçimindeydi.</summary>
    [Fact]
    public async Task Save_then_load_preserves_labor_flags_and_cost_unit()
    {
        var unitId = await GetAnyUnitIdAsync();

        using (_currentCompany.Change(FixtureCompanyId))
        {
            var created = await _metalAppService.CreateAsync(new MetalCreateDto
            {
                Code = "LABORFLAG",
                Name = "Bayrak Testi Madeni",
                FollowingUnitId = unitId,
            });

            var variant = created.Variants.ShouldHaveSingleItem();
            variant.LaborType = MetalLaborType.Amount;
            variant.LaborTypeChange = true;
            variant.EntryLabor = 12m;
            variant.EntryLaborUnitId = unitId;
            variant.EntryLaborChange = true;
            variant.ExitLabor = 18m;
            variant.ExitLaborUnitId = unitId;
            variant.ExitLaborChange = true;
            variant.CostUnitId = unitId;

            await UpdateAsync(created, unitId, variant);

            // 1) İlk okuma — bayraklar geri gelmeli (yazma yolu + graf okuma yolu birlikte).
            var loaded = await _metalAppService.GetAsync(created.Id);
            var loadedVariant = loaded.Variants.ShouldHaveSingleItem();
            loadedVariant.LaborTypeChange.ShouldBeTrue();
            loadedVariant.EntryLaborChange.ShouldBeTrue();
            loadedVariant.ExitLaborChange.ShouldBeTrue();
            loadedVariant.CostUnitId.ShouldBe(unitId);

            // 2) DOKUNMADAN ikinci kayıt — canlıdaki senaryo: kullanıcı formu açıp Kaydet'e basıyor.
            //    Eski kod burada üç bayrağı da false'a, CostUnitId'yi null'a çekiyordu.
            await UpdateAsync(loaded, unitId, loadedVariant);

            var reloaded = await _metalAppService.GetAsync(created.Id);
            var reloadedVariant = reloaded.Variants.ShouldHaveSingleItem();
            reloadedVariant.LaborTypeChange.ShouldBeTrue("İkinci Save bayrağı SİLDİ — veri kaybı geri geldi.");
            reloadedVariant.EntryLaborChange.ShouldBeTrue();
            reloadedVariant.ExitLaborChange.ShouldBeTrue();
            reloadedVariant.CostUnitId.ShouldBe(unitId);
            reloadedVariant.EntryLabor.ShouldBe(12m);
            reloadedVariant.ExitLabor.ShouldBe(18m);
        }
    }

    /// <summary>Liste ve picker yolları bayrakları TAŞIMALI — cari işlem paneli işçilik kilidini bu alanlardan
    /// okur; taşınmazsa panel her madende "işçilik salt-okunur" davranır (ACIK-ISLER:53 #4 regresyonu).</summary>
    [Fact]
    public async Task List_and_picker_carry_labor_change_flags()
    {
        var unitId = await GetAnyUnitIdAsync();

        using (_currentCompany.Change(FixtureCompanyId))
        {
            var created = await _metalAppService.CreateAsync(new MetalCreateDto
            {
                Code = "LABORLIST",
                Name = "Liste Bayrak Madeni",
                FollowingUnitId = unitId,
            });

            var variant = created.Variants.ShouldHaveSingleItem();
            variant.EntryLaborChange = true;
            variant.ExitLaborChange = true;
            variant.LaborTypeChange = true;
            variant.EntryLabor = 5m;
            await UpdateAsync(created, unitId, variant);

            var listed = (await _metalAppService.GetListAsync(new MetalListRequestDto { MaxResultCount = 200 }))
                .Items.Single(m => m.Id == created.Id);
            listed.EntryLaborChange.ShouldBeTrue();
            listed.ExitLaborChange.ShouldBeTrue();
            listed.LaborTypeChange.ShouldBeTrue();

            var picked = (await _metalAppService.GetPickerListAsync()).Single(m => m.Id == created.Id);
            picked.EntryLaborChange.ShouldBeTrue();
            picked.ExitLaborChange.ShouldBeTrue();
            picked.LaborTypeChange.ShouldBeTrue();
        }
    }

    /// <summary>Varyant picker'ı HER VARYANTIN KENDİ bayrağını döndürmeli — A4'ün (fiş VariantId=B kaydedip
    /// A'nın işçiliğini tahsil etme) veri-kaynağı ayağı. Panel code-behind'i test edilemez; kaynak burada pinlenir.</summary>
    [Fact]
    public async Task Variant_picker_carries_flags_per_variant()
    {
        var unitId = await GetAnyUnitIdAsync();

        using (_currentCompany.Change(FixtureCompanyId))
        {
            var created = await _metalAppService.CreateAsync(new MetalCreateDto
            {
                Code = "LABORVAR",
                Name = "Çok Varyantlı Maden",
                FollowingUnitId = unitId,
            });

            // İKİ varyant NİTELİK üzerinden kurulur: niteliksiz kayıtta synchronizer bilinçle TEK ana varyant
            // bırakır (elle eklenen ikinci satır kalıcılaşmaz) — kartezyen tek meşru çoğaltma yoludur.
            await _metalAppService.UpdateAsync(created.Id, new MetalUpdateDto
            {
                Code = created.Code,
                Name = created.Name,
                FollowingUnitId = unitId,
                Factor = created.Factor,
                IsActive = created.IsActive,
                Attributes = new List<EntityAttributeGraphDto>
                {
                    new()
                    {
                        Name = "Ayar",
                        Values = new List<EntityAttributeValueGraphDto>
                        {
                            new() { Value = "22K" },
                            new() { Value = "14K" },
                        },
                    },
                },
                Variants = new List<MetalVariantGraphDto>(),
            });

            // Kartezyen iki varyant üretti; her birine FARKLI bayrak yazılır.
            var withVariants = await _metalAppService.GetAsync(created.Id);
            withVariants.Variants.Count.ShouldBe(2);

            var first = withVariants.Variants[0];
            var second = withVariants.Variants[1];
            first.EntryLabor = 10m;
            first.EntryLaborChange = true;
            second.EntryLabor = 20m;
            second.EntryLaborChange = false;

            await _metalAppService.UpdateAsync(created.Id, new MetalUpdateDto
            {
                Code = withVariants.Code,
                Name = withVariants.Name,
                FollowingUnitId = unitId,
                Factor = withVariants.Factor,
                IsActive = withVariants.IsActive,
                Attributes = withVariants.Attributes,
                Variants = withVariants.Variants,
            });

            var options = await _metalAppService.GetVariantPickerListAsync(created.Id);
            options.Count.ShouldBe(2);

            options.Single(o => o.Id == first.Id).EntryLaborChange
                .ShouldBeTrue("Varyantın KENDİ bayrağı taşınmadı.");
            options.Single(o => o.Id == second.Id).EntryLaborChange
                .ShouldBeFalse("Bayrak başka varyanttan kopyalanmış — A4 hatası (fiş yanlış işçilik tahsil eder).");
        }
    }

    private Task UpdateAsync(MetalGetDto metal, Guid unitId, params MetalVariantGraphDto[] variants)
    {
        return _metalAppService.UpdateAsync(metal.Id, new MetalUpdateDto
        {
            Code = metal.Code,
            Name = metal.Name,
            FollowingUnitId = unitId,
            Factor = metal.Factor,
            IsActive = metal.IsActive,
            Variants = variants.ToList(),
        });
    }

    private async Task<Guid> GetAnyUnitIdAsync()
    {
        return await WithUnitOfWorkAsync(async () => (await _unitRepository.GetListAsync()).First().Id);
    }
}
