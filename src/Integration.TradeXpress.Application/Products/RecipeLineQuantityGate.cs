using System;
using Integration.TradeXpress.Vouchers;
using Volo.Abp;

namespace Integration.TradeXpress.Products;

/// <summary>
/// REÇETE GİRİŞ GUARD'I — "ana emtia 0 adet / 0 miktar girilmez" kuralının FIRLATAN ucu (2026-08-19 Hakan kuralı).
/// Kuralın kendisi <see cref="RecipeLineQuantityRule"/>'da (Domain); burası yalnız ihlali tek biçimde
/// <see cref="BusinessException"/>'a çevirir ki ürün reçetesi yazıcısı ile reçete şablonu birleştiricisi aynı
/// kodu + aynı veriyi üretsin (iki yerde ayrı throw yazılsaydı biri "LineOrder", diğeri "Line" derdi ve
/// lokalizasyon metni birinde boş kalırdı).
///
/// <para><b>Fail-fast:</b> çağıran <see cref="EnsureSatisfied"/>'ı HİÇBİR satırı yazmadan ÖNCE tüm satırlar için çağırır — kısmi
/// yazım (ilk iki satır yazıldı, üçüncü reddedildi) reçeteyi yarım bırakırdı.</para>
/// </summary>
public static class RecipeLineQuantityGate
{
    public const string ZeroQuantityErrorCode = "TradeXpress:Product:Recipe:ZeroQuantity";

    /// <summary>Katalog emtiası satırında adet ya da miktardan en az biri pozitif değilse fırlatır; hizmet
    /// satırında (kapsam dışı) hiçbir şey yapmaz. <paramref name="lineOrder"/> sıfır tabanlıdır, mesajda
    /// kullanıcıya 1 tabanlı satır numarası gösterilir.</summary>
    public static void EnsureSatisfied(
        RecipeComponentType componentType,
        decimal quantity,
        decimal amount,
        int lineOrder,
        ProcessType? commodityFamily)
    {
        if (RecipeLineQuantityRule.IsSatisfied(componentType, quantity, amount))
        {
            return;
        }

        throw new BusinessException(ZeroQuantityErrorCode)
            .WithData("LineOrder", lineOrder + 1)
            .WithData("Commodity", commodityFamily?.ToString() ?? string.Empty);
    }
}

/// <summary>
/// REÇETE GİRİŞ GUARD'I — "katalog emtiası satırı emtiasız olamaz" kuralının FIRLATAN ucu (2026-08-21).
/// Kuralın kendisi <see cref="RecipeLineCommodityRule"/>'da (Domain); <see cref="RecipeLineQuantityGate"/> ile
/// AYNI dosyada ve AYNI imzada durur çünkü ikisi tek bir denetim turunda yan yana çağrılır — ayrı dosyalara
/// dağıtmak, çağıranın birini ekleyip diğerini unutmasını kolaylaştırırdı.
///
/// <para><b>Neden fırlatır, işaretlemez:</b> emtiasız katalog satırı sessiz YANLIŞ SONUÇ üretir (maliyet eksik,
/// stok tetiği ürünü hiç uyandırmaz). Kaydı kabul edip satışa hazırlık panelinde uyarmak yetmezdi: satır o ana
/// kadar maliyet hesabına ve push'a çoktan girmiş olurdu.</para>
/// </summary>
public static class RecipeLineCommodityGate
{
    public const string CommodityRequiredErrorCode = "TradeXpress:Product:Recipe:CommodityRequired";

    /// <summary>Katalog emtiası satırında katalog kaydı seçilmemişse fırlatır; hizmet satırında (kapsam dışı)
    /// hiçbir şey yapmaz. <paramref name="lineOrder"/> sıfır tabanlıdır, mesajda kullanıcıya 1 tabanlı satır
    /// numarası gösterilir — <see cref="RecipeLineQuantityGate.EnsureSatisfied"/> ile aynı veri seti
    /// (LineOrder + Commodity) ki iki hata aynı biçimde okunabilsin.</summary>
    public static void EnsureSatisfied(
        RecipeComponentType componentType,
        Guid? commodityId,
        int lineOrder,
        ProcessType? commodityFamily)
    {
        if (RecipeLineCommodityRule.IsSatisfied(componentType, commodityId))
        {
            return;
        }

        throw new BusinessException(CommodityRequiredErrorCode)
            .WithData("LineOrder", lineOrder + 1)
            .WithData("Commodity", commodityFamily?.ToString() ?? string.Empty);
    }
}
