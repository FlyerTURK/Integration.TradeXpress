using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Reports;

/// <summary>
/// Bilanço raporu. Akış: kapsam(Branch/Company)+tarih seç → <see cref="ComputeAsync"/> ("Bilanço Al" — canlı hesapla,
/// KAYDETMEZ) → kullanıcı önizler → <see cref="SaveAsync"/> ("Kaydet" — snapshot'a yazar). Kategoriler pluggable.
/// </summary>
public interface IBalanceSheetReportAppService : IApplicationService
{
    /// <summary>Kapsam+tarih için bilançoyu CANLI hesaplar (kalıcı yazmaz). Toolbar "Bilanço Al".</summary>
    Task<BalanceSheetReportResultDto> ComputeAsync(BalanceSheetReportFilterDto filter);

    /// <summary>Hesaplayıp SNAPSHOT'a yazar (aynı kapsam+tarih varsa üzerine). Toolbar "Kaydet". Yazılan sonucu döner.</summary>
    Task<BalanceSheetReportResultDto> SaveAsync(BalanceSheetReportFilterDto filter);

    /// <summary>DRILL — bir kategori×birim değerinin oluştuğu HAREKETLER (çift-tık popup; Kod=belge no, Bakiye). BAKİYE(cari) destekli; diğerleri SONRA.</summary>
    Task<BalanceSheetMovementResultDto> GetMovementsAsync(BalanceSheetMovementRequestDto input);
}
