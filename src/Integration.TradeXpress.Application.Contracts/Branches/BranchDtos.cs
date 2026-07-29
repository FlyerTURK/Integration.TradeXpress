using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Addressing;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Integration.TradeXpress.Vaults;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Branches;

/// <summary>Şube adresi — <see cref="Integration.Framework.Addressing.Address"/> VO'nun düz (flat) yansıması,
/// ortak <c>AddressFields</c> bileşenine bind için <see cref="IAddressEditModel"/>. Picker İl/İlçe/Mahalle +
/// kodları + id-only köprüleri doldurur; serbest-metin yalnız Line/PostalCode/Title.</summary>
public class BranchAddressDto : IAddressEditModel
{
    public string? Title { get; set; }
    [Required]
    public string City { get; set; } = string.Empty;
    [Required]
    public string Line { get; set; } = string.Empty;
    public string? District { get; set; }
    public string? Neighborhood { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "TR";

    /// <summary>Ülke ADI — salt görüntü (adres özetinde kod yerine "Türkiye"). Otoriter alan CountryCode'dur.</summary>
    public string? CountryName { get; set; }
    public string? CityCode { get; set; }
    public string? DistrictCode { get; set; }
    public Guid? AdministrativeAreaId { get; set; }
    public Guid? LocalityId { get; set; }
    public string? AdministrativeAreaIsoCode { get; set; }
    public string? BuildingName { get; set; }
    public string? BuildingNumber { get; set; }
    public string? Room { get; set; }
    public string? Floor { get; set; }
    public string? Postbox { get; set; }
    public string? AdditionalStreetName { get; set; }

    /// <summary>Tam deep-copy (graf clone'unda "unutulan alan" bug'ı olmasın — VaultGraphDto.Clone deseni).</summary>
    public BranchAddressDto Clone()
    {
        return new BranchAddressDto
        {
            Title = Title,
            City = City,
            Line = Line,
            District = District,
            Neighborhood = Neighborhood,
            PostalCode = PostalCode,
            CountryCode = CountryCode,
            CityCode = CityCode,
            DistrictCode = DistrictCode,
            AdministrativeAreaId = AdministrativeAreaId,
            LocalityId = LocalityId,
            AdministrativeAreaIsoCode = AdministrativeAreaIsoCode,
            BuildingName = BuildingName,
            BuildingNumber = BuildingNumber,
            Room = Room,
            Floor = Floor,
            Postbox = Postbox,
            AdditionalStreetName = AdditionalStreetName,
        };
    }
}

/// <summary>Branch liste sorgusu (per-tenant). Merkezi <see cref="ListRequestDto"/> standardı.</summary>
public class BranchListRequestDto : ListRequestDto
{
    /// <summary>Drill-down filtresi: yalnızca bu şirkete ait şubeler. GET'te scalar serialize olur.</summary>
    public Guid? CompanyId { get; set; }
}

public class BranchListDto : EntityDto<Guid>, IListDto<Guid>, IIsActive
{
    public Guid CompanyId { get; set; }
    public string CompanyCode { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;

    /// <summary>Şubenin şirketinin ülkesi (Company.CountryId) — adres formu ülke VARSAYILANI için (özel gönderim/iade
    /// adresinde ülke serbest ama company ülkesine ön-dolar). Legacy şirkette null olabilir.</summary>
    public Guid? CompanyCountryId { get; set; }

    public Guid BaseCurrencyUnitId { get; set; }
    public string BaseCurrencyCode { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsHeadquarters { get; set; }
    // IsActive: ana grid kolonu kaldırıldı ama Company drill list'i (BranchTreeItemViewModel)
    // bu listeden besleniyor ve durumu gösteriyor; bu yüzden DTO'da kalır.
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }

    /// <summary>Şubenin posta adresi (opsiyonel; null → adres yok) — YALNIZ picker path (<c>GetMyCompanyBranchesAsync</c>)
    /// doldurur; genel grid listesinde null. Kargo şablonu ŞUBE modunda adres özetini besler + inline düzenlemenin
    /// başlangıç değeri (ülke şubenin şirket ülkesine kilitli).</summary>
    public BranchAddressDto? Address { get; set; }

    /// <summary>Combo kapalı gösterimi: "ŞirketKodu / ŞubeKodu".</summary>
    public string CompanyBranchCode => $"{CompanyCode} / {Code}";

    /// <summary>Combo 1. kolon: "ŞirketKodu / ŞirketAdı".</summary>
    public string CompanyDisplay => $"{CompanyCode} / {CompanyName}";

    /// <summary>Combo 2. kolon: "ŞubeKodu / ŞubeAdı".</summary>
    public string BranchDisplay => $"{Code} / {Name}";
}

public class BranchGetDto : EntityDto<Guid>, IGetDto<Guid>
{
    public Guid CompanyId { get; set; }
    public string CompanyCode { get; set; } = string.Empty;

    /// <summary>Şubenin değerleme (bilanço) birimi (ZORUNLU; varsayılan = parent şirketin base'i).</summary>
    public Guid BaseCurrencyUnitId { get; set; }

    // VALİDASYON kuralları BURADA (tek kaynak) — BranchGraphDto bunlardan MİRAS alır → standalone ve
    // company-node şube düzenlemeleri GARANTİLİ aynı kuralları doğrular.
    [Required]
    [StringLength(BranchConsts.CodeMaxLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(BranchConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    public bool IsHeadquarters { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }

    [StringLength(BranchConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    /// <summary>Şube posta adresi (opsiyonel; null → adres yok). Server okur/yazar; standalone formda AddressFields bind eder.</summary>
    public BranchAddressDto? Address { get; set; }

    /// <summary>Şubenin şirketinin ülkesi (Company.CountryId) — adres formunun FixedCountryId'si (ülke kilidi).
    /// Server doldurur (Branch.CompanyId → Company.CountryId çözülür). Legacy şirkette null olabilir (kilit yok).</summary>
    public Guid? CompanyCountryId { get; set; }

    // Sahip olunan kasalar (graf düğümleri; durum = Id + IsDeleted). Edit formu in-memory yönetir.
    public List<VaultGraphDto> Vaults { get; set; } = new();
}

public class BranchCreateDto : ICreateDto
{
    [Required]
    public Guid CompanyId { get; set; }

    /// <summary>Bilanço birimi — boş Guid → parent şirketin base'i (AppService default'lar).</summary>
    public Guid BaseCurrencyUnitId { get; set; }

    [Required]
    [StringLength(BranchConsts.CodeMaxLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(BranchConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    public bool IsHeadquarters { get; set; }
    public int DisplayOrder { get; set; }

    [StringLength(BranchConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    /// <summary>Şube posta adresi (opsiyonel; null → adres yok). Ülke AppService'te şirketin ülkesine zorlanır.</summary>
    public BranchAddressDto? Address { get; set; }

    // Sahip olunan kasalar (graf) — tek komutta yazılır (VaultAppService'e delege).
    public List<VaultGraphDto> Vaults { get; set; } = new();
}

// Parent (CompanyId) güncellemede değişmez — hiyerarşi sabit.
public class BranchUpdateDto : IUpdateDto
{
    /// <summary>Bilanço birimi — şubenin kendi değerleme birimi (boş Guid → mevcut korunur).</summary>
    public Guid BaseCurrencyUnitId { get; set; }

    [Required]
    [StringLength(BranchConsts.CodeMaxLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(BranchConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    public bool IsHeadquarters { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }

    [StringLength(BranchConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    /// <summary>Şube posta adresi (opsiyonel; null → adres yok/temizle). Ülke AppService'te şirketin ülkesine zorlanır.
    /// Company-grafı yolu mevcut adresi faithful round-trip eder (silme YOK); standalone form kendi adresini gönderir.</summary>
    public BranchAddressDto? Address { get; set; }

    // Sahip olunan kasalar (graf; Id+IsDeleted ile diff) — tek komutta yazılır (VaultAppService'e delege).
    public List<VaultGraphDto> Vaults { get; set; } = new();
}

/// <summary>
/// Company grafının şube DÜĞÜMÜ — Company edit'inde in-memory drill + Company save'i içindir (kendi
/// app servisi YOK; standalone Branch CRUD ayrı: <see cref="BranchGetDto"/> vb.). Durum = <see cref="Id"/>
/// + <see cref="IsDeleted"/>: Id boş → ekle, IsDeleted → sil, aksi → güncelle. Kasalar <see cref="Vaults"/>.
/// </summary>
public class BranchGraphDto : BranchGetDto, IHasIsActive
{
    // Graf düğümü EKSTRALARI (durum). Code/Name/Vaults + TÜM VALİDASYON BranchGetDto'dan MİRAS → standalone
    // ve company-node şube düzenlemeleri tek kaynaktan, GARANTİLİ aynı (kopya yok). (K3: GraphDto : GetDto)
    public Guid ClientKey { get; set; } = Guid.NewGuid();
    public bool IsDeleted { get; set; }

    /// <summary>Tam deep-copy (kasalar dahil). TÜM alanlar tek yerde → "unutulan alan" clone bug'ı imkânsız.</summary>
    public BranchGraphDto Clone() => new()
    {
        Id = Id, ClientKey = ClientKey, IsDeleted = IsDeleted,
        CompanyId = CompanyId, CompanyCode = CompanyCode, CompanyCountryId = CompanyCountryId,
        BaseCurrencyUnitId = BaseCurrencyUnitId,
        Code = Code, Name = Name, IsHeadquarters = IsHeadquarters,
        IsActive = IsActive, DisplayOrder = DisplayOrder, Description = Description,
        Address = Address?.Clone(),
        Vaults = Vaults.ConvertAll(v => v.Clone()),
    };
}
