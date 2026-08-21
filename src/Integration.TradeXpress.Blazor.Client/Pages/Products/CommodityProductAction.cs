using System;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Microsoft.Extensions.Localization;

namespace Integration.TradeXpress.Blazor.Client.Pages.Products;

/// <summary>
/// EMTİA LİSTELERİNDEKİ "ÜRÜN OLUŞTUR" AKSİYONU — yedi ailede AYNI düğme, TEK tanım
/// (2026-08-20; <c>ProductCommoditySeed</c>'in ters yöndeki karşılığı).
///
/// <para><b>Neden ortak:</b> aynı düğme yedi liste sayfasında çiziliyor. Metin/ikon/sıra/etkinlik kuralı
/// yedi kez yazılsaydı biri sessizce sapardı — bu projede tam bu desen defalarca yaşandı. Sapma görünmez
/// olurdu: düğme yine çizilir, yalnız başka yerde durur ya da seçim şartını atlar.</para>
///
/// <para><b>TEK KAYIT ŞARTI (<c>selectedCount == 1</c>):</b> çoklu seçimde hangi emtiadan ürün üretileceği
/// belirsizdir; sessizce ilkini seçmek kullanıcının GÖRMEDİĞİ bir tercih olurdu. Toplu üretim ayrı bir
/// karardır.</para>
///
/// <para><b>Seçim yokken GİZLENMEZ, devre dışı kalır:</b> görünmeyen düğme "böyle bir şey yok" der, soluk
/// düğme "bir kayıt seç" der — ikincisi doğru bilgidir (mamül listesindeki ikizinin gerekçesiyle aynı).</para>
///
/// <para><b>Metin aile-NÖTRDÜR</b> (<c>Commodity:CreateProduct</c>): mamülün kendi anahtarı
/// (<c>Good:CreateProduct</c>) yerinde bırakıldı — çalışan bir metni değiştirmek onay ister ve mamülün
/// açıklaması ("mamülün görselleri ve varyantları") kendi bağlamında daha somuttur.</para>
///
/// <para><b>Statik, servis DEĞİL:</b> client projesindeki DI kayıtları server modülünde ELLE yapılmak
/// zorunda (client modülü server'ın <c>DependsOn</c> zincirinde değil) ve unutulunca bileşen
/// <c>[Inject]</c> anında circuit'i düşürüyor. Durumu olmayan bir kurala o riski almanın anlamı yok —
/// <c>ProductCommoditySeed</c>'in de statik olma gerekçesi budur.</para>
/// </summary>
public static class CommodityProductAction
{
    /// <summary>Toolbar slotu — stok "Sil"(100) ile "Arama"(400) arasındaki custom aralık. Mamüldeki
    /// ikiziyle AYNI sayı: aynı düğme yedi listede aynı yerde durmalı.</summary>
    public const int SortIndex = 300;

    /// <summary>Aile-nötr düğme metni.</summary>
    public const string TextKey = "Commodity:CreateProduct";

    /// <summary>Aile-nötr ipucu — "kayıt açılmaz" beyanı burada yaşar.</summary>
    public const string TooltipKey = "Commodity:CreateProductTooltip";

    /// <summary>Aksiyonu kurar; <paramref name="selectedCount"/> tam olarak 1 değilse düğme SOLUK çizilir.</summary>
    public static CrudToolbarAction Build(IStringLocalizer localizer, int selectedCount, Func<Task> onClick)
    {
        return new CrudToolbarAction
        {
            SortIndex = SortIndex,
            Text = localizer[TextKey],
            Tooltip = localizer[TooltipKey],
            IconCssClass = TradeXpressIcons.Product + " xaf-toolbar-item-icon",
            Enabled = selectedCount == 1,
            OnClick = onClick,
        };
    }
}
