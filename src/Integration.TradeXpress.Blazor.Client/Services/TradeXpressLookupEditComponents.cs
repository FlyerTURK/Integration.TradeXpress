using System;
using System.Collections.Generic;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.AddOns;
using Integration.TradeXpress.AssayOffices;
using Integration.TradeXpress.Blazor.Client.Pages.Accounts;
using Integration.TradeXpress.Blazor.Client.Pages.AddOns;
using Integration.TradeXpress.Blazor.Client.Pages.AssayOffices;
using Integration.TradeXpress.Blazor.Client.Pages.Companies;
using Integration.TradeXpress.Blazor.Client.Pages.Countries;
using Integration.TradeXpress.Blazor.Client.Pages.Financials.CurrencyUnits;
using Integration.TradeXpress.Blazor.Client.Pages.Futures;
using Integration.TradeXpress.Blazor.Client.Pages.Goods;
using Integration.TradeXpress.Blazor.Client.Pages.Jewelries;
using Integration.TradeXpress.Blazor.Client.Pages.Metals;
using Integration.TradeXpress.Blazor.Client.Pages.ProductCategories;
using Integration.TradeXpress.Blazor.Client.Pages.RecipeTemplates;
using Integration.TradeXpress.Blazor.Client.Pages.Scraps;
using Integration.TradeXpress.Blazor.Client.Pages.Services;
using Integration.TradeXpress.Blazor.Client.Pages.SpecialCodes;
using Integration.TradeXpress.Blazor.Client.Pages.Stones;
using Integration.TradeXpress.Blazor.Client.Pages.Substitutions;
using Integration.TradeXpress.Blazor.Client.Pages.VariantTemplates;
using Integration.TradeXpress.Blazor.Client.Pages.Vaults;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Countries;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Futures;
using Integration.TradeXpress.Goods;
using Integration.TradeXpress.Jewelries;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.ProductCategories;
using Integration.TradeXpress.RecipeTemplates;
using Integration.TradeXpress.Scraps;
using Integration.TradeXpress.Services;
using Integration.TradeXpress.SpecialCodes;
using Integration.TradeXpress.Stones;
using Integration.TradeXpress.Substitutions;
using Integration.TradeXpress.VariantTemplates;
using Integration.TradeXpress.Vaults;

namespace Integration.TradeXpress.Blazor.Client.Services;

/// <summary>
/// Lookup listesi DTO'su → düzenleme host'u eşlemesi (uygulamaya özel; Framework hiçbir uygulama tipini tanımaz).
///
/// <para><b>Kural (2026-08-07 Hakan):</b> <c>LookupComboBox</c>'ta ekle/düzelt düğmeleri <b>varsayılan
/// GÖRÜNÜR</b>dür — <i>"bu component zaten bunun için var; yoksa standart combo zaten işimizi çok rahat
/// görüyor"</i>. Eskiden düğmeler yalnız çağıran <c>EditComponentType</c> yazdığında çiziliyordu ve 69
/// kullanımın ancak 15'i yazıyordu.</para>
///
/// <para><b>Neden tek tablo, çağrı-yeri yaması değil:</b> aynı satırı 50+ dosyaya eklemek birini unutmayı
/// garanti eder ve unutulan yerde hata görünmez — yalnız düğmesiz bir combo kalır. Burada eksik olan tip
/// derleme zamanında görünür ve konvansiyon testiyle sürülür.</para>
///
/// <para><b>Listede OLMAYAN tip düğmesiz kalır</b> ve bu bilinçli olabilir: türetilmiş/salt-okuma lookup'ları
/// (<c>CurrentPriceDto</c>, <c>MetalVariantLookupDto</c>, <c>MyVaultDto</c>, <c>CommodityVariantOptionDto</c>)
/// düzenlenebilir bir KAYIT değildir — onların "ekle" düğmesi anlamsız olurdu.</para>
/// </summary>
public static class TradeXpressLookupEditComponents
{
    public static ILookupEditComponentRegistry Build()
    {
        return new LookupEditComponentRegistry(new Dictionary<Type, Type>
        {
            [typeof(CurrencyUnitListDto)]      = typeof(CurrencyUnitEditHost),
            [typeof(AccountListDto)]           = typeof(AccountEditHost),
            [typeof(SubAccountListDto)]        = typeof(SubAccountEditHost),
            [typeof(BranchListDto)]            = typeof(BranchEditHost),
            [typeof(VaultListDto)]             = typeof(VaultEditHost),
            [typeof(CountryListDto)]           = typeof(CountryEditHost),
            [typeof(AssayOfficeListDto)]       = typeof(AssayOfficeEditHost),
            [typeof(AddOnListDto)]             = typeof(AddOnEditHost),
            [typeof(SpecialCodeListDto)]       = typeof(SpecialCodeEditHost),
            [typeof(ProductCategoryListDto)]   = typeof(ProductCategoryEditHost),
            [typeof(RecipeTemplateListDto)]    = typeof(RecipeTemplateEditHost),
            [typeof(VariantTemplateListDto)]   = typeof(VariantTemplateEditHost),
            [typeof(SubstitutionGroupListDto)] = typeof(SubstitutionGroupEditHost),

            // Emtia aileleri — reçete/süreç panellerindeki lookup'lar buradan düğme kazanır.
            [typeof(MetalListDto)]             = typeof(MetalEditHost),
            [typeof(ScrapListDto)]             = typeof(ScrapEditHost),
            [typeof(FutureListDto)]            = typeof(FutureEditHost),
            [typeof(GoodListDto)]              = typeof(GoodEditHost),
            [typeof(JewelryListDto)]           = typeof(JewelryEditHost),
            [typeof(StoneListDto)]             = typeof(StoneEditHost),
            [typeof(ServiceListDto)]           = typeof(ServiceEditHost),
        });
    }
}
