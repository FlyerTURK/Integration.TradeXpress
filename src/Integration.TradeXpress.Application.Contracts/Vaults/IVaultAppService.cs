using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Vaults;

public interface IVaultAppService : ICrudAppService<
    VaultGetDto,
    VaultListDto,
    Guid,
    VaultListRequestDto,
    VaultCreateDto,
    VaultUpdateDto>
{
    // NOT (2026-07-15 ürün kararı): GetCurrentAccountAsync EMEKLİ — kasayı sahte bir cariye (vault-cari)
    // çözüyordu. Kasa artık fişte doğrudan karşı taraftır (Voucher.AccountType=Vault) → çözüm gerekmez.

    /// <summary>
    /// Kullanıcının ÇALIŞABİLDİĞİ kasalar (<c>IBranchAppService.GetMyBranchesAsync</c>'in kasa karşılığı) —
    /// server-side kapsam daraltması: her satır <c>ScopedAccessSet.CanAccessVault</c> ile elenir. Working-context
    /// kasa seçicisi ve fiş formunun kasa listesi bunu tüketir. Client'a güvenilmez; filtre sunucudadır.
    /// <para><paramref name="branchId"/> verilirse yalnız o şubenin kasaları (fiş formu: "o şubede + yetkili
    /// olduğun" kasalar).</para>
    /// </summary>
    Task<List<MyVaultDto>> GetMyVaultsAsync(Guid? branchId = null);
}
