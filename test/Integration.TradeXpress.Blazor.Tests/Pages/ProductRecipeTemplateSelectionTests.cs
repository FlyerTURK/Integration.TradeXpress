using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bunit;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.Blazor.Client.Components.Shared;
using Integration.TradeXpress.Blazor.Client.Pages.Products;
using Integration.TradeXpress.N11Categories;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.RecipeTemplates;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Blazor.Tests.Pages;

/// <summary>
/// Ürün formundaki REÇETE ŞABLONU bloğunun görünürlük sözleşmesi (2026-08-20 Hakan kararları).
///
/// <para><b>Kural:</b> şablon seçimi HER üründen beklenir — bu yüzden combo YENİ üründe de çizilir.
/// "Uygula" düğmesi ise yalnız KAYITLI üründe anlamlıdır: kaydedilmemiş üründe uygulanacak varyant henüz
/// yoktur, basılabilir bir düğme sessizce hiçbir şey yapardı. Yeni üründe uygulama ilk kayda ertelenir
/// (<c>ProductEditHost.OnAfterCreate</c>) ve bu erteleme kullanıcıya ipucu metniyle SÖYLENİR.</para>
///
/// <para><b>Neden render testi:</b> blok eskiden <c>@if (!IsNew)</c> ile TAMAMEN gizliydi; gizli bir alanın
/// geri gelmesi derleme hatası vermez — yalnız gerçek render markup'ı bunu kanıtlar. Düğmenin gizlenmesi de
/// <c>Visible</c> ile yapıldığından (ui-blazor: nested settings öğesi <c>@if</c> ile sarılmaz) markup'ta
/// varlığı/yokluğu tek doğrulanabilir imzadır.</para>
///
/// <para>Combo'nun kendisi DevExpress editör iç yapısı üzerinden metinle aranamaz (bUnit'te editör içeriği
/// JS tarafında doğar) → bileşen AĞACINDA aranır: <c>FindComponents</c> tip-güvenli ve DevExpress
/// markup'ından bağımsızdır.</para>
/// </summary>
public class ProductRecipeTemplateSelectionTests : BlazorComponentTestBase
{
    // Pass-through lokalizasyon anahtarı AYNEN döndürdüğü için anahtarlar markup imzası olarak kullanılır.
    private const string ApplyButtonKey = "RecipeTemplate:ApplyButton";
    private const string ApplyHintKey = "RecipeTemplate:ApplyHint";
    private const string ApplyOnSaveHintKey = "RecipeTemplate:ApplyOnSaveHint";

    private static readonly Guid TemplateId = Guid.NewGuid();

    private readonly IProductAppService _productAppService;

    public ProductRecipeTemplateSelectionTests()
    {
        // Ürün formunun render'ına giren alt bileşenlerin servisleri (kanal kategori seçici · varyant
        // medyası · satışa hazırlık paneli). Davranışları sınanmıyor — amaç ağacın kurulabilmesi.
        AddSubstitute<IN11CategoryAppService>();
        AddSubstitute<IMediaAppService>();
        _productAppService = AddSubstitute<IProductAppService>();
        AddUiInteraction();
    }

    [Fact]
    public void A_new_product_offers_the_recipe_template_combo()
    {
        var component = RenderLayout(NewProductModel(Guid.Empty));

        component.FindComponents<LookupComboBox<RecipeTemplateListDto, Guid?>>().ShouldNotBeEmpty();
    }

    [Fact]
    public void A_new_product_hides_the_apply_button_and_says_why()
    {
        var component = RenderLayout(NewProductModel(Guid.Empty));

        // Düğme YOK: uygulanacak varyant henüz yok.
        component.Markup.ShouldNotContain(ApplyButtonKey);

        // Ama SESSİZ değil: uygulamanın ilk kayda ertelendiği yazıyla söylenir. Düğmeyi göremeyen kullanıcı
        // aksi hâlde seçimin işe yaramadığını sanardı.
        component.Markup.ShouldContain(ApplyOnSaveHintKey);
    }

    [Fact]
    public void A_saved_product_keeps_the_apply_button()
    {
        var component = RenderLayout(NewProductModel(Guid.NewGuid()));

        // Kayıtlı üründe davranış DEĞİŞMEDİ: combo + Uygula düğmesi + mevcut ipucu.
        component.FindComponents<LookupComboBox<RecipeTemplateListDto, Guid?>>().ShouldNotBeEmpty();
        component.Markup.ShouldContain(ApplyButtonKey);
        component.Markup.ShouldContain(ApplyHintKey);
        component.Markup.ShouldNotContain(ApplyOnSaveHintKey);
    }

    /// <summary>
    /// KADEMELİ KİLİT EDİTÖRE ULAŞIR MI (2026-08-20 regresyonu). Kilit ilk sürümde
    /// <c>DxFormLayoutItem Enabled=</c> üzerindeydi; DevExpress'te bu özellik yalnız OTOMATİK ÜRETİLEN
    /// editörleri kapsadığından (bu formdaki editörlerin hepsi elle bildirilmiş) kilit KOZMETİKTİ —
    /// alanlar açık kalıyordu ve kullanıcı kategori→kod→ad sırasını atlayabiliyordu. Hata da vermiyordu,
    /// yani yalnız editörün kendi <c>Enabled</c>'ını okumak bunu kanıtlar.
    /// </summary>
    [Fact]
    public void The_gradual_lock_reaches_the_editor_itself()
    {
        // Yeni ürün, AD henüz girilmemiş → "geri kalan her şey" (şablon combo'su dahil) kapalı olmalı.
        var model = NewProductModel(Guid.Empty);
        model.Name = string.Empty;

        var combo = RenderLayout(model).FindComponent<LookupComboBox<RecipeTemplateListDto, Guid?>>();

        combo.Instance.Enabled.ShouldBeFalse();
    }

    [Fact]
    public void The_gradual_lock_opens_once_the_identity_fields_are_filled()
    {
        // Aynı combo, kod + ad dolu: kilit AÇILIR. Kilidin sadece "hep kapalı" olmadığını da sabitler.
        var combo = RenderLayout(NewProductModel(Guid.Empty)).FindComponent<LookupComboBox<RecipeTemplateListDto, Guid?>>();

        combo.Instance.Enabled.ShouldBeTrue();
    }

    /// <summary>
    /// ÜRÜN GENELİ BANDI ENDEKSİ GÖRÜYOR MU (2026-08-20 regresyonu). Bant formun EN ÜSTÜNDE, issue endeksini
    /// cascade eden <c>CascadingValue</c> ise yalnız sekmeleri sarıyordu → bant cascade'in DIŞINDA kalıyor,
    /// <c>CascadingParameter</c> null geliyor ve <c>ReadinessNotice</c> hiç markup üretmiyordu. Sessiz ölümdü:
    /// derleme geçiyor, hata çıkmıyor, yalnız uyarı görünmüyordu — "her üründe şablon seçilmedi uyarısı"
    /// (Hakan 2026-08-20) fiilen yalnız satışa hazırlık paneli sekmesinde karşılanıyordu.
    ///
    /// <para>Assert BANDIN KENDİ markup'ına bakar, sayfanın tamamına değil: aynı mesajı satışa hazırlık paneli de
    /// listeler, tüm sayfada aramak bant ölü olsa bile YEŞİL kalırdı.</para>
    /// </summary>
    [Fact]
    public void The_general_notice_band_reads_the_cascaded_readiness_index()
    {
        var productId = Guid.NewGuid();
        const string message = "Reçete şablonu seçilmedi.";

        _productAppService.GetSaleReadinessAsync(productId).Returns(Task.FromResult(new ProductSaleReadinessDto
        {
            ProductId = productId,
            Issues = new List<SaleReadinessIssueDto>
            {
                new()
                {
                    Severity = SaleReadinessSeverity.Warning,
                    Code = "Product:NoRecipeTemplate",
                    Message = message,
                    StepKey = "Recipe",
                    Path = SaleReadinessScope.General,
                },
            },
        }));

        var component = RenderLayout(NewProductModel(productId));

        var band = component.FindComponents<ReadinessNotice>()
            .First(c => c.Instance.Scope == SaleReadinessScope.General);

        band.Markup.ShouldContain(message);
    }

    private IRenderedComponent<ProductLayout> RenderLayout(ProductGetDto model)
    {
        return Render<ProductLayout>(parameters => parameters
            .Add(p => p.Model, model)
            .Add(p => p.IsNew, model.Id == Guid.Empty)
            .Add(p => p.RecipeTemplates, new List<RecipeTemplateListDto>
            {
                new() { Id = TemplateId, Name = "Standart Masraflar", IsActive = true },
            }));
    }

    /// <summary>Kategori + kod + ad DOLU: yeni üründe kademeli kilit bu üç koşuldan sonra formun geri
    /// kalanını (şablon combo'su dahil) açar. Dolu vermezsek combo çizilir ama devre dışı olur ve test
    /// görünürlük yerine kilidi ölçmüş olurdu.</summary>
    private static ProductGetDto NewProductModel(Guid id)
    {
        return new ProductGetDto
        {
            Id = id,
            Code = "URUN1",
            Name = "Ürün 1",
            ProductCategoryId = Guid.NewGuid(),
            IsActive = true,
        };
    }
}
