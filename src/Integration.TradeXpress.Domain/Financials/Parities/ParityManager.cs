namespace Integration.TradeXpress.Financials.Parities;

/// <summary>
/// Parite değişmezlerinin (invariant) sahibi — create'in TEK yolu (<c>CreateAsync</c>). Kural: bir çift sistemde TEK YÖNDE
/// yaşar (USDTRY varken TRYUSD olamaz). Kapsam = global (host, TenantId=null) + ilgili tenant; yön
/// kullanıcının seçtiği gibi korunur (canonicalize edilmez), yalnız ikinci yön engellenir.
///
/// <para>Garanti İKİ katman: (1) kapsamlı ön-kontrol → dostça lokalize hata; (2) DB'de
/// <c>(TenantId, PairKey)</c> yön-bağımsız unique index → ön-kontrolü geçen eşzamanlı yarışı da kapatır.
/// App kontrolü tek başına (check-then-insert) yarışa açıktır; asıl garantiyi DB verir.</para>
/// </summary>
public class ParityManager(
    IRepository<Parity, Guid> parityRepository,
    IDataFilter dataFilter)
    : DomainService
{
    #region Fields

    private readonly IRepository<Parity, Guid> _parityRepository = parityRepository;
    private readonly IDataFilter _dataFilter = dataFilter;

    #endregion

    #region Methods

    /// <summary>Pariteyi kurar (create'in TEK yolu): kapsamlı ön-kontrol + insert.</summary>
    public async Task<Parity> CreateAsync(
        Guid baseCurrencyUnitId,
        Guid quoteCurrencyUnitId,
        bool isActive,
        int displayOrder,
        Guid? tenantId)
    {
        await EnsureCreatableAsync(baseCurrencyUnitId, quoteCurrencyUnitId, tenantId);

        var parity = new Parity(
            baseCurrencyUnitId: baseCurrencyUnitId,
            quoteCurrencyUnitId: quoteCurrencyUnitId,
            isActive: isActive,
            displayOrder: displayOrder);

        // Ön-kontrolü geçen eşzamanlı yarış kalsa bile (TenantId, PairKey) unique index integrity'i korur.
        return await _parityRepository.InsertAsync(parity, autoSave: true);
    }

    /// <summary>
    /// Çiftin oluşturulabilirliğini doğrular (fail-fast, dostça hata). Aynı çift → <c>PairAlreadyExists</c>,
    /// ters çift (quote/base) → <c>ReversePairAlreadyExists</c>, base==quote → <c>BaseQuoteMustDiffer</c>.
    /// </summary>
    public async Task EnsureCreatableAsync(Guid baseCurrencyUnitId, Guid quoteCurrencyUnitId, Guid? tenantId)
    {
        if (baseCurrencyUnitId == quoteCurrencyUnitId)
        {
            throw new BusinessException("TradeXpress:Parity:BaseQuoteMustDiffer");
        }

        // Kapsam host‖own — tenant da global pariteyi (ve tersini) ikinci kez oluşturamaz.
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var scoped = (await _parityRepository.GetQueryableAsync())
                .Where(p => p.TenantId == null || p.TenantId == tenantId);

            var forwardExists = await AsyncExecuter.AnyAsync(scoped.Where(p =>
                p.BaseCurrencyUnitId == baseCurrencyUnitId && p.QuoteCurrencyUnitId == quoteCurrencyUnitId));
            if (forwardExists)
            {
                throw new BusinessException("TradeXpress:Parity:PairAlreadyExists");
            }

            var reverseExists = await AsyncExecuter.AnyAsync(scoped.Where(p =>
                p.BaseCurrencyUnitId == quoteCurrencyUnitId && p.QuoteCurrencyUnitId == baseCurrencyUnitId));
            if (reverseExists)
            {
                throw new BusinessException("TradeXpress:Parity:ReversePairAlreadyExists");
            }
        }
    }

    #endregion
}
