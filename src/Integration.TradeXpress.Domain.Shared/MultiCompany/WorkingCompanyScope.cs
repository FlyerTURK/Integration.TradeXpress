using System;
using System.Collections.Generic;
using System.Linq;

namespace Integration.TradeXpress.MultiCompany;

/// <summary>
/// Working-context şirket seçiminin SUNUCU-TARAFI zorlama kuralı (SSOT) — sahte/yetkisiz seçimi izinli
/// kümeye indirger. SAF fonksiyon (ILogger/DI taşımaz): Blazor köprüsü
/// (<c>WorkingCompanyContextProvider</c>) ile testler AYNI kuralı paylaşır. Domain.Shared'da yaşar → hem
/// WASM client projesi hem de test projesi erişebilir (provider Domain'e referans veremez; kural burada tek).
///
/// <para><b>Kural (fail-forward / fail-closed):</b> seçili şirket izinli kümedeyse aynen döner; değilse
/// İLK izinli şirkete düşülür — <c>null</c>'a DEĞİL (<c>CurrentCompanyId==null</c> = konsolide = TÜM tenant
/// görünür = TERS güvenlik). Hiç izinli şirket yoksa <see cref="Guid.Empty"/> sentinel: owned kayıt
/// görünmez (hiçbir gerçek CompanyId Guid.Empty değil), paylaşılan/host katalog görünür kalır — filtre
/// null-permissive tuzağına düşmez.</para>
/// </summary>
public static class WorkingCompanyScope
{
    /// <summary>Filtreye verilecek EFEKTİF şirket id'i. ASLA null döndürmez (fail-forward ya da sentinel).</summary>
    public static Guid ResolveEffectiveCompanyId(Guid? selectedCompanyId, IReadOnlyList<Guid> allowedCompanyIds)
    {
        if (selectedCompanyId is { } selected && allowedCompanyIds.Contains(selected))
        {
            return selected;
        }

        // İzinli şirket varsa ilkine düş; yoksa sentinel (erişim yok — owned gizli, katalog görünür).
        return allowedCompanyIds.Count > 0 ? allowedCompanyIds[0] : Guid.Empty;
    }

    /// <summary>Seçim yetkisiz mi (dolu ama izinli kümede yok)? Provider'ın uyarı log'u için.</summary>
    public static bool IsUnauthorizedSelection(Guid? selectedCompanyId, IReadOnlyList<Guid> allowedCompanyIds)
    {
        return selectedCompanyId is { } selected && !allowedCompanyIds.Contains(selected);
    }
}
