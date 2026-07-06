namespace Integration.TradeXpress.Branches;

/// <summary>
/// Şirketin (<see cref="Companies.Company"/>) bir şubesi — OrgScope'un orta seviyesi. Tek parent
/// (<see cref="CompanyId"/>, id-only referans; nav YOK, aggregate sınırı). Her şirket en az bir
/// <see cref="IsHeadquarters"/> (merkez) şubeyle doğar; şube oluşturulurken otomatik bir
/// <see cref="Vaults.Vault"/> (kasa) açılır. Per-tenant (IMultiTenant).
///
/// <para>HQ devri: bir şubeyi HQ yapmak, şirketin önceki HQ şubesini düşürür (AppService doğrular,
/// şirket başına tek HQ). HQ şube, HQ başka bir şubeye devredilmedikçe silinemez; ayrıca şirketin
/// son şubesi silinemez (en az 1 child kuralı).</para>
/// </summary>
public class Branch : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Üst şirket — id-only referans (nav YOK).</summary>
    public virtual Guid CompanyId { get; protected set; }

    /// <summary>Değerleme (bilanço) para birimi — global CurrencyUnit'e id-only referans (nav YOK).
    /// ZORUNLU (non-null); varsayılan = parent şirketin base'i (AppService resolve eder).</summary>
    public virtual Guid BaseCurrencyUnitId { get; protected set; }

    public virtual string Code { get; protected set; } = null!;

    public virtual string Name { get; protected set; } = null!;

    /// <summary>Şirketin merkez (HQ) şubesi mi. Şirket başına tek HQ (AppService doğrular).</summary>
    public virtual bool IsHeadquarters { get; protected set; }

    public virtual bool IsActive { get; protected set; }

    /// <summary>P&L (gider/gelir) dönem-başlangıç sınırı — ERPPRO <c>Subeler.RevCostDate</c> muadili. Bu tarihten
    /// SONRAKİ (strict) gider/gelir cari döneme sayılır; öncekiler kapanmış sayılır. null = hiç kapanmadı (hepsi dahil,
    /// mevcut davranış). YALNIZ P&L kaynağını (ServicePL) sınırlar; net-varlık kaynaklarını ETKİLEMEZ.
    /// <para><b>Wall-clock (kaymasız):</b> date-only sınır; <c>[DisableDateTimeNormalization]</c> + <see cref="SetProfitResetDate"/>
    /// içinde <see cref="BusinessClock.AsBusinessDate"/> → ServicePL'deki <c>VoucherDate &gt; ProfitResetDate</c> karşılaştırması
    /// aynı Kind (Unspecified) üzerinde kalır, dönem kesme günü kaymaz.</para></summary>
    [DisableDateTimeNormalization]
    public virtual DateTime? ProfitResetDate { get; protected set; }

    public virtual int DisplayOrder { get; protected set; }
    public virtual string? Description { get; protected set; }

    protected Branch() { }

    public Branch(
        Guid companyId,
        string code,
        string name,
        bool isHeadquarters = false,
        int displayOrder = 0)
    {
        SetCompany(companyId);
        SetCode(code);
        SetName(name);
        IsHeadquarters = isHeadquarters;
        DisplayOrder = displayOrder;
        IsActive = true;
    }

    public virtual void SetCode(string code)
    {
        // NormalizeCode: Trim + çoklu boşluk→tek + UPPER (boşluk KORUNUR), ardından zorunlu/min/max doğrulaması.
        // Elle .ToUpperInvariant() gerekmez (NormalizeCode zaten UPPER yapar).
        Code = StringFieldGuard.NormalizeCode(
            code,
            nameof(Code),
            EntityFieldConsts.CodeMinLength,
            BranchConsts.CodeMaxLength);
    }

    public virtual void SetName(string name)
    {
        // NormalizeName: Trim + çoklu boşluk→tek + TitleCase, ardından zorunlu/min/max doğrulaması.
        Name = StringFieldGuard.NormalizeName(
            name,
            nameof(Name),
            EntityFieldConsts.NameMinLength,
            BranchConsts.NameMaxLength);
    }

    public virtual void SetCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new BusinessException("TradeXpress:Branch:CompanyRequired");
        }

        CompanyId = companyId;
    }

    /// <summary>Bilanço/değerleme birimi (ZORUNLU). Boş Guid reddedilir (AppService önce şirket base'ine düşürür).</summary>
    public virtual void SetBaseCurrency(Guid baseCurrencyUnitId)
    {
        if (baseCurrencyUnitId == Guid.Empty)
        {
            throw new BusinessException("TradeXpress:Branch:BaseCurrencyRequired");
        }

        BaseCurrencyUnitId = baseCurrencyUnitId;
    }

    public virtual void SetDescription(string? description)
    {
        // Opsiyonel alan: yalnız üst sınır (min yok — mevcut davranış korunur). Aşılırsa tipli Framework exception'ı.
        if (description is { Length: > BranchConsts.DescriptionMaxLength })
        {
            throw new TooLongPropertyException(nameof(Description), BranchConsts.DescriptionMaxLength);
        }

        Description = description;
    }

    public virtual void SetActive(bool value)
    {
        IsActive = value;
    }

    /// <summary>P&L dönem-başlangıç sınırını ilerletir (dönem kapanışı — ERPPRO RevCostDate update muadili).</summary>
    public virtual void SetProfitResetDate(DateTime value)
    {
        // Date-only wall-clock: saat atılır + Kind=Unspecified (dönem sınırı günü kaymaz).
        ProfitResetDate = BusinessClock.AsBusinessDate(value);
    }

    public virtual void SetAsHeadquarters(bool isHeadquarters)
    {
        IsHeadquarters = isHeadquarters;
    }

    public virtual void SetDisplayOrder(int order)
    {
        DisplayOrder = order;
    }

    public override string ToString()
    {
        return Code;
    }
}
