using System;
using System.Linq;
using Integration.TradeXpress.Blazor.Client.Pages.Companies.Models;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.Vaults;

namespace Integration.TradeXpress.Blazor.Client.Pages.Companies;

/// <summary>CompanyTree DTO ↔ ViewModel manuel eşleme (ClientKey + nullable Id nedeniyle elle).
/// Kasalar in-memory tutulur; düzenleme KARDEŞ popup'ta yapılır (şube edit formuna gömülü DEĞİL).</summary>
public static class CompanyTreeMapping
{
    public static CompanyViewModel ToViewModel(CompanyTreeDto src) => new()
    {
        Id = src.Id,
        Name = src.Name,
        CountryCode = src.CountryCode,
        BaseCurrencyUnitId = src.BaseCurrencyUnitId,
        IsActive = src.IsActive,
        IsHeadquarters = src.IsHeadquarters,
        DisplayOrder = src.DisplayOrder,
        Description = src.Description,
        ConcurrencyStamp = src.ConcurrencyStamp,
        Branches = src.Branches.Select(b => new BranchTreeItemViewModel
        {
            // Mevcut kayıt için ClientKey = sunucu Id'si (deterministik): reload'da grid satır kimliği
            // sabit kalır → seçim sürekliliği + ileride optimistic-merge köprüsü için stabil anahtar.
            ClientKey = b.Id,
            Id = b.Id,
            Name = b.Name,
            IsHeadquarters = b.IsHeadquarters,
            IsActive = b.IsActive,
            DisplayOrder = b.DisplayOrder,
            Description = b.Description,
            ConcurrencyStamp = b.ConcurrencyStamp,
            Vaults = b.Vaults.Select(v => new VaultTreeItemViewModel
            {
                ClientKey = v.Id,  // deterministik: mevcut kasanın grid kimliği = sunucu Id'si
                Id = v.Id,
                Name = v.Name,
                IsDefault = v.IsDefault,
                IsActive = v.IsActive,
                DisplayOrder = v.DisplayOrder,
                Description = v.Description,
                ConcurrencyStamp = v.ConcurrencyStamp,
            }).ToList(),
        }).ToList(),
    };

    public static CompanyTreeSaveDto ToSaveDto(CompanyViewModel vm) => new()
    {
        Id = vm.Id == Guid.Empty ? null : vm.Id,
        Name = vm.Name,
        CountryCode = vm.CountryCode,
        BaseCurrencyUnitId = vm.BaseCurrencyUnitId,
        IsActive = vm.IsActive,
        IsHeadquarters = vm.IsHeadquarters,
        DisplayOrder = vm.DisplayOrder,
        Description = vm.Description,
        ConcurrencyStamp = vm.ConcurrencyStamp,
        DeletedBranchIds = vm.DeletedBranchIds.ToList(),
        Branches = vm.Branches.Select(b => new BranchTreeSaveDto
        {
            Id = b.Id,
            Name = b.Name,
            IsHeadquarters = b.IsHeadquarters,
            IsActive = b.IsActive,
            DisplayOrder = b.DisplayOrder,
            Description = b.Description,
            ConcurrencyStamp = b.ConcurrencyStamp,
            DeletedVaultIds = b.DeletedVaultIds.ToList(),
            Vaults = b.Vaults.Select(v => new VaultTreeSaveDto
            {
                Id = v.Id,
                Name = v.Name,
                IsDefault = v.IsDefault,
                IsActive = v.IsActive,
                DisplayOrder = v.DisplayOrder,
                Description = v.Description,
                ConcurrencyStamp = v.ConcurrencyStamp,
            }).ToList(),
        }).ToList(),
    };

    /// <summary>Yeni şirket için varsayılan in-memory ağaç: bir HQ "Merkez Şube" + bir "Ana Kasa".</summary>
    public static BranchTreeItemViewModel NewHeadquartersBranch() => new()
    {
        ClientKey = Guid.NewGuid(),
        Name = BranchConsts.DefaultHeadquartersName,
        IsHeadquarters = true,
        IsActive = true,
        DisplayOrder = 1,
        Vaults = { NewDefaultVault() },
    };

    public static VaultTreeItemViewModel NewDefaultVault() => new()
    {
        ClientKey = Guid.NewGuid(),
        Name = VaultConsts.DefaultName,
        IsDefault = true,
        IsActive = true,
        DisplayOrder = 1,
    };

    // ── Düzenleme için derin kopya (DrillList Cancel'da geri alabilsin; canlı nesne mutasyonu YOK) ──

    public static BranchTreeItemViewModel CloneBranch(BranchTreeItemViewModel b) => new()
    {
        ClientKey = b.ClientKey,  // aynı grid anahtarı → seçim/satır korunur
        Id = b.Id,
        Name = b.Name,
        IsHeadquarters = b.IsHeadquarters,
        IsActive = b.IsActive,
        DisplayOrder = b.DisplayOrder,
        Description = b.Description,
        ConcurrencyStamp = b.ConcurrencyStamp,
        Vaults = b.Vaults.Select(CloneVault).ToList(),
        DeletedVaultIds = b.DeletedVaultIds.ToList(),
    };

    public static VaultTreeItemViewModel CloneVault(VaultTreeItemViewModel v) => new()
    {
        ClientKey = v.ClientKey,
        Id = v.Id,
        Name = v.Name,
        IsDefault = v.IsDefault,
        IsActive = v.IsActive,
        DisplayOrder = v.DisplayOrder,
        Description = v.Description,
        ConcurrencyStamp = v.ConcurrencyStamp,
    };
}
