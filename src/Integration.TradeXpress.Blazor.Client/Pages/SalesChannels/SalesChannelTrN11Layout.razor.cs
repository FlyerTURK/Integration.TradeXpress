using Integration.TradeXpress.SalesChannels;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.SalesChannels;

/// <summary>N11 dumb layout code-behind — yalnız Model bağlama (I/O yok).</summary>
public partial class SalesChannelTrN11Layout
{
    [Parameter, EditorRequired] public SalesChannelTrN11GetDto Model { get; set; } = default!;
    [Parameter] public bool IsNew { get; set; }

    /// <summary>Düzenlemede sir alanları boş gelir → in-field ipucu "saklı, boş = korunur"; yeni kayıtta placeholder yok.</summary>
    private string? SecretPlaceholder => IsNew ? null : L["SalesChannel:SecretKept"].Value;

    // Giderler formu ve onu besleyen SideCosts getter'ı 2026-07-28'de KALDIRILDI. Bu getter'ın kalması
    // TEHLİKELİYDİ: ayarı hiç yapılandırılmamış (null) kanalda boş bir DTO üretip kayda {"Items":[]} yazdırıyordu
    // ve o değer "kullanıcı komisyon satırını sildi" anlamına geldiği için komisyon fiyata HİÇ girmiyordu —
    // hatasız, logsuz, yalnız ~%23 ucuz fiyat. Ayar artık null kalıyor; komisyonu kategori oranından örtük olarak
    // SideCostPlan.From üretiyor (SideCostRecipeComposerTests'teki iki test bunu çiviliyor).
}
