using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Blazor.Client.Pages.Products;
using Microsoft.Extensions.Localization;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Blazor.Tests.Pages;

/// <summary>
/// EMTİA LİSTELERİNDEKİ "ÜRÜN OLUŞTUR" AKSİYONUNUN SÖZLEŞMESİ (<see cref="CommodityProductAction"/>).
///
/// <para><b>Neden burada ve neden bUnit DEĞİL:</b> düğme yedi liste sayfasının toolbar'ında yaşıyor ve o
/// sayfaların tamamı <c>CrudLayout</c> + <c>DxGrid</c> + server-mode veri kaynağı istiyor — sayfayı bUnit'te
/// çizmek, sınanmak istenen kuralı (tek seçim şartı) DevExpress altyapısının arkasına gömerdi. Kural aksiyon
/// fabrikasında TEK yerde yaşadığı için doğrudan orada sınanır; sayfa tarafında geriye kalan tek şey bu
/// fabrikayı çağırmaktır.</para>
///
/// <para><b>Sabitlenen üç şey:</b> ① seçim yokken/çokken düğme SOLUK ama VAR (görünmeyen düğme "böyle bir şey
/// yok" der, soluk düğme "bir kayıt seç" der); ② metin AİLE-NÖTR anahtardan gelir — mamülün kendi anahtarına
/// (<c>Good:CreateProduct</c>) düşülmez; ③ tıklama geri çağrısı aynen taşınır (yutulmaz).</para>
/// </summary>
public class CommodityProductActionTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(7, false)]
    public void The_action_is_enabled_only_for_exactly_one_selected_record(int selectedCount, bool expectedEnabled)
    {
        // ÇOKLU seçimde hangi emtiadan ürün üretileceği belirsizdir; sessizce ilkini seçmek kullanıcının
        // GÖRMEDİĞİ bir tercih olurdu. Toplu üretim ayrı bir karardır.
        var action = CommodityProductAction.Build(NewLocalizer(), selectedCount, () => Task.CompletedTask);

        action.Enabled.ShouldBe(expectedEnabled);
        action.Visible.ShouldBeTrue("Seçim yokken düğme GİZLENMEZ, yalnız devre dışı kalır.");
    }

    [Fact]
    public void The_action_uses_the_family_neutral_caption_not_the_good_specific_one()
    {
        // Aile-nötr anahtar: aynı düğme Maden/Hurda/Vadeli/Mücevher/Taş/Hizmet listelerinde de çiziliyor.
        var action = CommodityProductAction.Build(NewLocalizer(), selectedCount: 1, () => Task.CompletedTask);

        action.Text.ShouldBe("Commodity:CreateProduct");
        action.Tooltip.ShouldBe("Commodity:CreateProductTooltip");
        action.Text.ShouldNotBe("Good:CreateProduct");
    }

    [Fact]
    public void The_action_sits_in_the_shared_custom_slot_with_the_product_icon()
    {
        // Aynı düğme yedi listede AYNI yerde durmalı; ikon merkezi setten gelir (ad-hoc sembol YASAK).
        var action = CommodityProductAction.Build(NewLocalizer(), selectedCount: 1, () => Task.CompletedTask);

        action.SortIndex.ShouldBe(300);
        action.IconCssClass.ShouldNotBeNullOrWhiteSpace();
        action.IconCssClass!.ShouldStartWith("custom-icon-");
    }

    [Fact]
    public async Task Clicking_the_action_runs_the_page_callback_once()
    {
        var clicks = 0;
        var action = CommodityProductAction.Build(
            NewLocalizer(),
            selectedCount: 1,
            () =>
            {
                clicks++;
                return Task.CompletedTask;
            });

        action.OnClick.ShouldNotBeNull();
        await action.OnClick!();

        clicks.ShouldBe(1);
    }

    /// <summary>Anahtarı olduğu gibi döndüren geçirgen localizer — testin ilgisi çeviri METNİ değil, hangi
    /// ANAHTARIN kullanıldığıdır (çeviri json'ları ayrı bir mekanik ağın, parite testinin, konusu).</summary>
    private static IStringLocalizer NewLocalizer()
    {
        return new PassThroughLocalizer();
    }

    private sealed class PassThroughLocalizer : IStringLocalizer
    {
        public LocalizedString this[string name]
        {
            get { return new LocalizedString(name, name, resourceNotFound: false); }
        }

        public LocalizedString this[string name, params object[] arguments]
        {
            get { return new LocalizedString(name, string.Format(CultureInfo.InvariantCulture, name, arguments), resourceNotFound: false); }
        }

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
        {
            return Enumerable.Empty<LocalizedString>();
        }
    }
}
