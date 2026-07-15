using System;

namespace Integration.TradeXpress.Vaults;

/// <summary>
/// Aktif (çalışılan) kasa — <see cref="MultiCompany.ICurrentCompany"/> deseninin kasa eşdeğeri. Değer
/// <see cref="IVaultContextProvider"/>'dan gelir (Blazor'da working-context köprüsü); sunucu işlemleri
/// <see cref="Change"/> ile geçici override edebilir.
///
/// <para><b>KASA GLOBAL QUERY FILTER'A BAĞLANMAZ (bağlayıcı karar):</b> bu ambient bir <b>ortam varsayılanı /
/// bağlamıdır, KISITLAMA değildir</b>. Kullanıcı birden çok kasaya hâkim olabilir; filtreye bağlansaydı bu
/// şart kırılırdı. Formların kasa seçicisi KALIR — bağlam yalnız varsayılanı önerir. Yetkiyi filtre değil
/// kapsam-grant'i (<c>ScopedAccessSet.CanAccessVault</c>) belirler.</para>
/// </summary>
public interface ICurrentVault
{
    Guid? Id { get; }

    /// <summary>Geçici kasa override scope'u (using ile geri alınır).</summary>
    IDisposable Change(Guid? vaultId);
}

/// <summary>
/// Aktif kasanın kaynağı. Varsayılan (host/API): null. Blazor circuit'inde working-context'e köprülenir.
/// </summary>
public interface IVaultContextProvider
{
    Guid? GetCurrentVaultId();
}
