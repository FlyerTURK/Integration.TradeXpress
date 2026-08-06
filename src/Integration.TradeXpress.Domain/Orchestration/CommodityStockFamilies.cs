using System.Linq;
using Integration.TradeXpress.Vouchers;

namespace Integration.TradeXpress.Orchestration;

/// <summary>
/// Stok zincirinin (tetik → ters-endeks → okuyucu → satılabilir adet) bugün KAPSADIĞI emtia aileleri.
/// <para>Tek liste, tek yer: tetiği yayan (<c>VoucherAppService</c>), ürünleri bulan
/// (<c>RecipeCommodityIndex</c>) ve stoğu okuyan (<see cref="ICommodityStockReader"/>) aynı kümeye bakmak
/// ZORUNDADIR — biri diğerinden geniş olursa ya boşa iş üretilir ya da bir aile sessizce zincirin dışında
/// kalır (oversell). Aile eklemek: buraya ekle + okuyucuda o ailenin rapor dalını aç.</para>
/// <para><b>Bugün Metal + Good.</b> Scrap raporunda emtia kırılımı yok; Jewelry/Stone/Future'ın rapor servisi
/// hiç yok — kapsama alınmaları ayrı iştir (rapor önce, zincir sonra).</para>
/// </summary>
public static class CommodityStockFamilies
{
    /// <summary>Kapsanan aileler. <b>Dizi bilerek</b>: EF sorgusunda <c>Contains</c> ile IN(...)'e çevrilir.</summary>
    public static readonly ProcessType[] Tracked = { ProcessType.Metal, ProcessType.Good };

    /// <summary>Bu aile stok zincirine giriyor mu (null = hizmet/manuel satır → hayır).</summary>
    public static bool IsTracked(ProcessType? family)
    {
        return family is { } value && Tracked.Contains(value);
    }
}
