using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Vouchers;

public interface IVoucherAppService : IApplicationService
{
    Task<VoucherGetDto> CreateAsync(VoucherCreateDto input);
    Task<PagedResultDto<VoucherListDto>> GetListAsync(VoucherListRequestDto input);

    /// <summary>Satır ekler/günceller; gerekirse fişi lazy oluşturur. Kaydedilen satırı (Id/VoucherId dolu) döndürür.</summary>
    Task<VoucherLineDto> SaveLineAsync(VoucherLineDto input);

    /// <summary>Bir fişin (silinmemiş) satırlarını döndürür.</summary>
    Task<List<VoucherLineDto>> GetLinesAsync(Guid voucherId);

    /// <summary>Liste modu: cari'nin [start, endExclusive) tarih aralığındaki tüm satırları (fiş-bağımsız) + yürüyen bakiye.
    /// (Ekstre metoduna delege eder; devreden/kapanış gerekiyorsa <see cref="GetAccountStatementAsync"/> kullan.)</summary>
    Task<List<VoucherLineDto>> GetLinesByDateRangeAsync(Guid subAccountId, DateTime start, DateTime endExclusive);

    /// <summary>Hesap ekstresi: [start, endExclusive) satırları + devreden/kapanış birim bakiyeleri.
    /// <paramref name="types"/> doluysa satırlar VE devreden/kapanış aynı işlem-tipi filtresiyle hesaplanır.</summary>
    Task<AccountStatementDto> GetAccountStatementAsync(Guid subAccountId, DateTime start, DateTime endExclusive, List<ProcessType>? types = null);

    /// <summary>Düzeltme için satırın tam halini döndürür.</summary>
    Task<VoucherLineDto> GetLineForEditAsync(Guid lineId);

    /// <summary>Satırı soft-delete eder (silme nedeni ile).</summary>
    Task DeleteLineAsync(Guid voucherId, Guid lineId, string reason);

    /// <summary>Karşı taraf için birim bazında anlık bakiye + bakiye birimi (opsiyonel tarih sınırı).
    /// <paramref name="subAccountId"/> POLİMORFİKTİR: cari kipinde SubAccount, iç kasa kipinde KASA id'si
    /// (fişin <c>AccountType</c>'ı belirler) → kasa bakiyeleri sahte cari olmadan aynı sözleşmeden okunur.</summary>
    Task<AccountBalanceDto> GetBalancesAsync(Guid subAccountId, DateTime? upTo = null);

    /// <summary>Bakiye Gösterim Modu = AccountScoped: <paramref name="accountId"/>'nin (cari kipte Account, iç
    /// kipte Şube) TÜM alt hesaplarının/kasalarının KONSOLİDE net bakiyesi + bakiye birimi. Tek bir alt hesap/kasa
    /// değil, aynı üst kimliğe bağlı hepsinin toplamı — <see cref="GetBalancesAsync"/>'in geniş-kapsamlı eşi.</summary>
    Task<AccountBalanceDto> GetAccountBalancesAsync(Guid accountId, DateTime? upTo = null);

    /// <summary>Takoz stoğu (aktif giriş külçeleri) — takoz ÇIKIŞ panelinin combo kaynağı.
    /// <paramref name="inStock"/> true → yalnız stokta olanlar (aktif çıkışı olmayan); null → hepsi (düzeltme için).</summary>
    Task<List<BullionStockItemDto>> GetBullionStockAsync(bool? inStock = null);

    /// <summary>Çeşni stoğu özeti (takoz girişlerinin AssayAmount havuzu − çeşni çıkışları) — Çeşni panelinin
    /// açılış ön-doldurma kaynağı. Milyemler ağırlıklı ortalama (Has/Miktar, legacy Cesni paritesi).</summary>
    Task<AssayStockDto> GetAssayStockAsync();

    /// <summary>Bir fişi (ve altındaki tüm satırları) siler.</summary>
    Task DeleteAsync(Guid id);

    /// <summary>Bakiye sekmesinde bir birime çift-tıklayınca açılan tarihçe: <paramref name="scopeIsAccount"/>
    /// false ise <paramref name="scopeId"/> SubAccount/Kasa (tek alt hesap/kasa — GetBalancesAsync ile aynı
    /// kapsam), true ise Account/Şube (konsolide — GetAccountBalancesAsync ile aynı kapsam). Yalnız
    /// <paramref name="unitId"/>'yi etkileyen satırlar döner (devreden + yürüyen net dahil).</summary>
    Task<UnitStatementDto> GetUnitStatementAsync(Guid scopeId, bool scopeIsAccount, Guid unitId, DateTime start, DateTime endExclusive);
}
