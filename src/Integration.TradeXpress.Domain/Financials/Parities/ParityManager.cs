namespace Integration.TradeXpress.Financials.Parities;

/// <summary>
/// Parite değişmezlerini (invariant) tek noktada toplar — böylece AppService ve seed yolları
/// aynı kuralı paylaşır. Asıl kural: <b>bir çift sistemde tek yönde yaşar</b> — USDTRY varken
/// TRYUSD oluşturulamaz. Kapsam = global (host, TenantId=null) + ilgili tenant; yön kullanıcının
/// seçtiği gibi korunur (canonicalize edilmez), yalnız <i>ikinci</i> yön engellenir.
/// </summary>
public class ParityManager : DomainService
{
    private readonly IRepository<Parity, Guid> _parityRepository;
    private readonly IDataFilter _dataFilter;

    public ParityManager(
        IRepository<Parity, Guid> parityRepository,
        IDataFilter dataFilter)
    {
        _parityRepository = parityRepository;
        _dataFilter = dataFilter;
    }

    /// <summary>
    /// Verilen çiftin oluşturulabilirliğini doğrular (fail-fast). Aynı çift varsa
    /// <c>PairAlreadyExists</c>, ters çift (quote/base) varsa <c>ReversePairAlreadyExists</c>,
    /// base==quote ise <c>BaseQuoteMustDiffer</c> fırlatır.
    /// </summary>
    public async Task EnsureCreatableAsync(Guid baseCurrencyUnitId, Guid quoteCurrencyUnitId, Guid? tenantId)
    {
        if (baseCurrencyUnitId == quoteCurrencyUnitId)
            throw new BusinessException("TradeXpress:Parity:BaseQuoteMustDiffer");

        // Kapsam host‖own — tenant da global pariteyi (ve tersini) ikinci kez oluşturamaz.
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var scoped = (await _parityRepository.GetQueryableAsync())
                .Where(p => p.TenantId == null || p.TenantId == tenantId);

            var forwardExists = await AsyncExecuter.AnyAsync(scoped.Where(p =>
                p.BaseCurrencyUnitId == baseCurrencyUnitId && p.QuoteCurrencyUnitId == quoteCurrencyUnitId));
            
            if (forwardExists)
                throw new BusinessException("TradeXpress:Parity:PairAlreadyExists");

            var reverseExists = await AsyncExecuter.AnyAsync(scoped.Where(p =>
                p.BaseCurrencyUnitId == quoteCurrencyUnitId && p.QuoteCurrencyUnitId == baseCurrencyUnitId));
            
            if (reverseExists)
                throw new BusinessException("TradeXpress:Parity:ReversePairAlreadyExists");
        }
    }
}
