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

    /// <summary>Bir cari (SubAccount) için birim bazında anlık bakiye + hesabın bakiye birimi (opsiyonel tarih sınırı).</summary>
    Task<AccountBalanceDto> GetBalancesAsync(Guid subAccountId, DateTime? upTo = null);

    /// <summary>Takoz stoğu (aktif giriş külçeleri) — takoz ÇIKIŞ panelinin combo kaynağı.
    /// <paramref name="inStock"/> true → yalnız stokta olanlar (aktif çıkışı olmayan); null → hepsi (düzeltme için).</summary>
    Task<List<BullionStockItemDto>> GetBullionStockAsync(bool? inStock = null);

    /// <summary>Çeşni stoğu özeti (takoz girişlerinin AssayAmount havuzu − çeşni çıkışları) — Çeşni panelinin
    /// açılış ön-doldurma kaynağı. Milyemler ağırlıklı ortalama (Has/Miktar, legacy Cesni paritesi).</summary>
    Task<AssayStockDto> GetAssayStockAsync();

    /// <summary>Bir fişi (ve altındaki tüm satırları) siler.</summary>
    Task DeleteAsync(Guid id);
}
