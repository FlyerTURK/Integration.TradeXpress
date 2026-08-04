using System;
using Bunit;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Blazor.Client.Pages.ProductCategories;
using Integration.TradeXpress.N11Categories;
using Integration.TradeXpress.ProductCategories;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Blazor.Tests.Pages;

/// <summary>
/// Kategori listesinin SEÇİCİ modu (lookup popup'ında açılış).
///
/// <para><b>Neden var:</b> "düzenle komutu görünmüyor" üç turda üç farklı sebeple aranıp bulunamadı
/// (koşullu EventCallback, kayıtlı grid düzeni, koşullu kolon üretimi). Ortak nokta: modun sayfaya
/// GERÇEKTEN ulaşıp ulaşmadığı hiç doğrulanmamıştı. Bu test o zinciri sabitler.</para>
/// </summary>
/// <remarks>
/// <b>bUnit SINIRI (2026-08-04, acı deneyimle öğrenildi):</b> DevExpress grid'i bUnit render'ında
/// <b>VERİ SATIRI ÇİZMEZ</b> — satırlar JS interop ile materyalize ediliyor ve test JSInterop'u loose kipte.
/// Dolayısıyla "şu kolon/düğme grid'de görünüyor mu" TÜRÜNDEN bir iddia burada SINANAMAZ; yazılırsa
/// düzeltmeden bağımsız olarak SONSUZA DEK KIRMIZI kalır (tam olarak bu yaşandı: seçici düzenle düğmesi
/// için yazılan render testi iki gün boyunca bir hata sandığımız şeyi değil, bu sınırı ölçtü).
/// Bu dosyadaki testler bilinçle PARAMETRE AKIŞINI doğrular, görsel çıktıyı değil.
/// </remarks>
public class ProductCategoryPickerModeTests : BlazorComponentTestBase
{
    public ProductCategoryPickerModeTests()
    {
        // Sayfa kendi uygulama servislerini [Inject] ile ister; amaç render, davranış değil.
        AddSubstitute<IProductCategoryAppService>();
        AddSubstitute<IN11CategoryAppService>();
        AddSubstitute<ICrudStateService<ProductCategoryListDto, Guid>>();
    }

    [Fact]
    public void List_page_accepts_picker_mode_parameter()
    {
        // Popup, sayfayı IsPickerMode=true ile açar (GlobalPopupHost → DynamicComponent parametreleri).
        // Parametre kabul edilmezse DynamicComponent "does not have a property" diye patlar — bu test onu yakalar.
        var component = Render<ProductCategoryListPage>(parameters => parameters
            .Add(p => p.IsPickerMode, true));

        component.Instance.IsPickerMode.ShouldBeTrue();
    }

    [Fact]
    public void List_page_defaults_to_normal_mode()
    {
        // Sekmede açılışta seçici davranışı DEVREDE OLMAMALI: satır tıklaması düzenlemeye gitmeli.
        var component = Render<ProductCategoryListPage>();

        component.Instance.IsPickerMode.ShouldBeFalse();
    }
}
