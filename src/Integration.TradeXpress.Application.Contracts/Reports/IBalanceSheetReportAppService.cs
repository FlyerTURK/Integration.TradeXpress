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

    /// <summary>Kaydedilmiş bilanço snapshot'larının GEÇMİŞ listesi (tarih PIVOT + DEVIR/KARZARAR/MASRAF/GUNLUK türetimi).
    /// Kapsam+company ICurrentCompany'den zorlanır. ERPPRO <c>BilancoListesi</c> paritesi. KURFARKI bu fazda YOK.</summary>
    Task<BalanceSheetSnapshotListDto> GetSnapshotListAsync(BalanceSheetSnapshotListRequestDto request);

    /// <summary>DÖNEM SIFIRLA (minimal, ERPPRO <c>FrmBilancoSifirla</c> kısmi muadili): kapsamdaki şube(ler)in
    /// <c>ProfitResetDate</c>'ini <c>filter.AsOf</c>'a ilerletir (P&L cari dönemi buradan başlar) + <see cref="SaveAsync"/>
    /// ile snapshot dondurur — AYNI UnitOfWork (atomik). CompanyId ICurrentCompany'den zorlanır. Branch scope → o şube;
    /// Company scope → şirketin TÜM şubeleri. RESMİ GL devir/prim kaydı YAPMAZ (o ayrı faz). Snapshot sonucunu döner.</summary>
    Task<BalanceSheetReportResultDto> ResetProfitPeriodAsync(BalanceSheetReportFilterDto filter);
}
