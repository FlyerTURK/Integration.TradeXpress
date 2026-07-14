using Integration.TradeXpress.Vouchers;
using Volo.Abp;

namespace Integration.TradeXpress.Permissions;

/// <summary>
/// <see cref="ProcessType"/> → cari işlem yetkisi (<see cref="TradeXpressPermissions.Transactions"/>) eşlemesi.
/// TEK KAYNAK: hem UI gate (Blazor.Client, buton görünürlüğü) hem server-side kontrol
/// (VoucherAppService) buradan okur — eşleme iki yerde ayrı ayrı tekrar edilmez (DRY).
/// </summary>
public static class ProcessTypePermissionMap
{
    /// <summary>Verilen işlem tipinin gerektirdiği yetki adını döndürür.</summary>
    public static string PermissionFor(ProcessType type) => type switch
    {
        ProcessType.Metal   => TradeXpressPermissions.Transactions.Metal,
        ProcessType.Scrap   => TradeXpressPermissions.Transactions.Scrap,
        ProcessType.Cash    => TradeXpressPermissions.Transactions.Cash,
        ProcessType.Convert => TradeXpressPermissions.Transactions.Convert,
        ProcessType.Service => TradeXpressPermissions.Transactions.Service,
        ProcessType.Future  => TradeXpressPermissions.Transactions.Future,
        ProcessType.Stone   => TradeXpressPermissions.Transactions.Stone,
        ProcessType.Jewelry => TradeXpressPermissions.Transactions.Jewelry,
        ProcessType.Good    => TradeXpressPermissions.Transactions.Good,
        ProcessType.Bullion => TradeXpressPermissions.Transactions.Bullion,
        ProcessType.Assay   => TradeXpressPermissions.Transactions.Assay,
        ProcessType.DebitNote => TradeXpressPermissions.Transactions.DebitNote,
        ProcessType.Transfer  => TradeXpressPermissions.Transactions.Transfer,
        // Fail-fast: bilinmeyen tip = hata
        // (sessiz geçme YOK). Ham .NET exception yerine error-code'lu + lokalize ABP BusinessException.
        _ => throw new BusinessException("TradeXpress:Transactions:UnknownProcessType")
            .WithData("type", type),
    };
}
