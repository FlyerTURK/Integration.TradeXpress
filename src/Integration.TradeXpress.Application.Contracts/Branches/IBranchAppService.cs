using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Branches;

public interface IBranchAppService : ICrudAppService<
    BranchGetDto,
    BranchListDto,
    Guid,
    BranchListRequestDto,
    BranchCreateDto,
    BranchUpdateDto>
{
    /// <summary>
    /// Working-context şube seçici için kullanıcının ERİŞEBİLDİĞİ şubeler (server-side kapsam daraltması;
    /// <see cref="Authorization.IScopedGrantResolver"/> ile). Client'a güvenilmez — filtre sunucuda uygulanır.
    /// </summary>
    Task<List<BranchListDto>> GetMyBranchesAsync();

    /// <summary>
    /// GEÇERLİ çalışılan şirketin AKTİF şubeleri — picker/lookup için (ör. kargo şablonu gönderim/iade şubesi).
    /// Sunucu <see cref="MultiCompany.ICurrentCompany"/> ile scope'lar (client CompanyId GÖNDERMEZ); yalnız
    /// kimliklendirilmiş kullanıcı yeter (Branches.Default gerekmez — company-owned katalog seçimi). Cross-company
    /// sızdırmaz (yanlış-şirket şubesi listelenmez).
    /// </summary>
    Task<List<BranchListDto>> GetMyCompanyBranchesAsync();

    /// <summary>
    /// Şubenin posta adresini GÜNCELLER — kargo şablonu ŞUBE modunda inline düzenleme (cross-entity yazma). Ülke
    /// şubenin ŞİRKET ülkesine KİLİTLİ (BuildAddressAsync; branch adresi company ülkesinde olmalı). Company-scope
    /// guard: <paramref name="branchId"/> GEÇERLİ çalışılan şirkete ait olmalı (aksi hâlde reddedilir). Güncel
    /// adres DTO'su döner (boş → <c>null</c>, adres temizlendi). Branches.Update yetkisi gerektirir.
    /// </summary>
    Task<BranchAddressDto?> UpdateAddressAsync(Guid branchId, BranchAddressDto address);
}
