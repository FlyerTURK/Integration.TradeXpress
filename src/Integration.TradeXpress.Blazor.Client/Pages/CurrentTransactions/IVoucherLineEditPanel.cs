using System.Threading.Tasks;
using Integration.TradeXpress.Vouchers;

namespace Integration.TradeXpress.Blazor.Client.Pages.CurrentTransactions;

/// <summary>Düzelt akışında fiş satırını yükleyebilen süreç panellerinin ortak sözleşmesi.
/// AccountSelectionPanel bu arayüz üzerinden tip-bazlı dispatch yapar (12'li else-if zinciri yerine).</summary>
internal interface IVoucherLineEditPanel
{
    /// <summary>Var olan fiş satırını panele düzenleme (UPDATE) modunda yükler.</summary>
    Task LoadForEditAsync(VoucherLineDto dto);

    /// <summary>İşlem geçmişindeki anlık görüntüyü SALT-OKUNUR yükler — kaydedilemez, ekler/karşı hesap
    /// gösterilmez (o günkü hâl gösteriliyor, bugünkü ekler değil).
    /// <para>VARSAYILAN: desteklemeyen panellerde hiçbir şey yapmaz. Salt-okunur kip
    /// <c>ProcessPanelHostBase</c>'te uygulanmıştır (Cash/Metal/Scrap/Service/Convert/Future);
    /// takoz/çeşni/dekont/virman panelleri kendi tabanlarını kullandığından henüz kapsam dışı —
    /// eklenmesi tek metot, ama şimdilik ihtiyaç doğmadı (YAGNI).</para></summary>
    Task LoadForViewAsync(VoucherLineDto snapshot) => Task.CompletedTask;

    /// <summary>Salt-okunur görüntüleme destekli mi — geçmiş çift tıklaması bunu kontrol eder ki
    /// desteklenmeyen tipte sessizce hiçbir şey olmuş gibi görünmesin.</summary>
    bool SupportsReadOnlyView => false;
}
