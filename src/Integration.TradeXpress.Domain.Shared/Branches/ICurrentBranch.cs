using System;

namespace Integration.TradeXpress.Branches;

/// <summary>
/// Aktif (çalışılan) şube — <see cref="MultiCompany.ICurrentCompany"/> deseninin şube eşdeğeri. Değer
/// <see cref="IBranchContextProvider"/>'dan gelir (Blazor'da bunu <c>WorkingBranchContextProvider</c> karşılar); sunucu işlemleri
/// <see cref="Change"/> ile geçici override edebilir (seed / cross-branch).
///
/// <para><b>Kapsam notu:</b> şube bir GLOBAL QUERY FILTER'a bağlı DEĞİLDİR; bu ambient, çalışma bağlamının
/// (ortam varsayılanı) sunucu tarafındaki okunur kaynağıdır — DTO'dan gelen client değerine güvenmek yerine.</para>
/// </summary>
public interface ICurrentBranch
{
    Guid? Id { get; }

    /// <summary>Geçici şube override scope'u (using ile geri alınır).</summary>
    IDisposable Change(Guid? branchId);
}

/// <summary>
/// Aktif şubenin kaynağı. Varsayılan (host/API): null. Blazor circuit'inde <c>WorkingBranchContextProvider</c>
/// bunu working-context'e bağlar.
/// </summary>
public interface IBranchContextProvider
{
    Guid? GetCurrentBranchId();
}
