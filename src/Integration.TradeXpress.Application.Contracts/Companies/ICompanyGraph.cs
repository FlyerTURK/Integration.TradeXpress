using System;
using System.Collections.Generic;
using Integration.TradeXpress.Branches;

namespace Integration.TradeXpress.Companies;

/// <summary>
/// Şirket-graf editörünün (paylaşılan <c>CompanyGraphEditor</c>) bağlandığı ortak yüzey. Hem standalone
/// <see cref="CompanyGetDto"/> hem tenant onboarding <see cref="CompanyGraphDto"/> bunu uygular →
/// editör tek kez yazılır, her iki bağlamda da kullanılır ("her yerde aynı form").
/// </summary>
public interface ICompanyGraph
{
    string Code { get; set; }
    string Name { get; set; }
    /// <summary>Ülke — Country'ye id-only referans (otoriter alan; editör combo'su buna bağlanır).</summary>
    Guid? CountryId { get; set; }
    Guid BaseCurrencyUnitId { get; set; }
    bool IsHeadquarters { get; set; }
    bool IsActive { get; set; }
    int DisplayOrder { get; set; }
    string? Description { get; set; }
    List<BranchGraphDto> Branches { get; set; }
}
