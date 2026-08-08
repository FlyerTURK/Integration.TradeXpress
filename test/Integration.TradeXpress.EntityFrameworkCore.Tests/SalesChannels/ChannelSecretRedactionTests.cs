using System;
using System.Threading.Tasks;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.MultiCompany;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.SalesChannels;

/// <summary>
/// KANAL SIRLARI CLIENT'A DÖNMEZ — üç pazaryerinin de <c>GetAsync</c>'i sır alanlarını BOŞ döndürür.
///
/// <para><b>Neden pin:</b> sözleşme bugüne kadar yalnız DTO'lardaki yorumlarla korunuyordu (bu oturumda
/// test grep'i = 0). <c>ObjectMapper.Map</c> entity'nin TÜM alanlarını taşır; redaksiyon satırını silen ya da
/// yeni bir sır alanı ekleyip redakte etmeyi unutan bir refactor hiçbir hata üretmez — sır sessizce
/// tarayıcıya iner ve orada kalır. Bu testler o refactor'ı KIRMIZI yakar.</para>
///
/// <para><b>Boş = "korunur" sözleşmesi:</b> güncelleme formunda sır alanları boş görünür; kullanıcı doldurursa
/// değişir, boş bırakırsa mevcut değer korunur. Yani redaksiyon veri kaybı DEĞİLDİR.</para>
///
/// <para><b>Kapsam:</b> bu dilim sırların at-rest ŞİFRELENMESİNİ yapmaz (DB'de düz metin duruyorlar — envanter
/// <c>.claude/research/rd-2026-08-07/pii-secrets-envanter.md</c>). Burada pinlenen şey yalnız client sınırı.</para>
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class ChannelSecretRedactionTests : TradeXpressEntityFrameworkCoreTestBase
{
    private const string Secret = "GIZLI-DEGER-1234";

    private readonly ISalesChannelTrN11AppService _n11Service;
    private readonly ISalesChannelTrTrendyolAppService _trendyolService;
    private readonly ISalesChannelEtsyAppService _etsyService;
    private readonly IRepository<SalesChannelTrN11, Guid> _n11Channels;
    private readonly IRepository<SalesChannelTrTrendyol, Guid> _trendyolChannels;
    private readonly IRepository<SalesChannelEtsy, Guid> _etsyChannels;
    private readonly TestCompanyContextProvider _companyContext;
    private readonly ICurrentTenant _currentTenant;

    public ChannelSecretRedactionTests()
    {
        _n11Service       = GetRequiredService<ISalesChannelTrN11AppService>();
        _trendyolService  = GetRequiredService<ISalesChannelTrTrendyolAppService>();
        _etsyService      = GetRequiredService<ISalesChannelEtsyAppService>();
        _n11Channels      = GetRequiredService<IRepository<SalesChannelTrN11, Guid>>();
        _trendyolChannels = GetRequiredService<IRepository<SalesChannelTrTrendyol, Guid>>();
        _etsyChannels     = GetRequiredService<IRepository<SalesChannelEtsy, Guid>>();
        _companyContext   = GetRequiredService<TestCompanyContextProvider>();
        _currentTenant    = GetRequiredService<ICurrentTenant>();
    }

    [Fact]
    public async Task N11_get_does_not_return_app_key_or_secret()
    {
        var scope = NewScope();

        using (_currentTenant.Change(scope.TenantId))
        {
            _companyContext.CompanyId = scope.CompanyId;

            var id = await WithUnitOfWorkAsync(async () =>
            {
                var channel = new SalesChannelTrN11(
                    scope.CompanyId, scope.Code, scope.Code, appKey: Secret, appSecret: Secret);
                await _n11Channels.InsertAsync(channel, autoSave: true);
                return channel.Id;
            });

            var dto = await WithUnitOfWorkAsync(() => _n11Service.GetAsync(id));

            dto.AppKey.ShouldBeEmpty();
            dto.AppSecret.ShouldBeEmpty();

            // Kontrol grubu: kayıt gerçekten sırrı TAŞIYOR — test boş bir kanalı doğrulamıyor.
            var stored = await WithUnitOfWorkAsync(() => _n11Channels.GetAsync(id));
            stored.AppSecret.ShouldBe(Secret);
        }
    }

    [Fact]
    public async Task Trendyol_get_does_not_return_api_key_secret_or_token()
    {
        var scope = NewScope();

        using (_currentTenant.Change(scope.TenantId))
        {
            _companyContext.CompanyId = scope.CompanyId;

            var id = await WithUnitOfWorkAsync(async () =>
            {
                var channel = new SalesChannelTrTrendyol(
                    scope.CompanyId, scope.Code, scope.Code, sellerId: "123456",
                    apiKey: Secret, apiSecret: Secret);
                await _trendyolChannels.InsertAsync(channel, autoSave: true);
                return channel.Id;
            });

            var dto = await WithUnitOfWorkAsync(() => _trendyolService.GetAsync(id));

            dto.ApiKey.ShouldBeEmpty();
            dto.ApiSecret.ShouldBeEmpty();
            // Token sırların TÜREVİDİR (base64(apiKey:apiSecret)) — redakte edilmezse sır dolaylı yoldan sızardı.
            dto.Token.ShouldBeEmpty();

            // SellerId sır DEĞİL (görünür satıcı kimliği) — redaksiyon fazla geniş olmamalı, form onu göstermeli.
            dto.SellerId.ShouldBe("123456");
        }
    }

    [Fact]
    public async Task Etsy_get_does_not_return_shared_secret()
    {
        var scope = NewScope();

        using (_currentTenant.Change(scope.TenantId))
        {
            _companyContext.CompanyId = scope.CompanyId;

            var id = await WithUnitOfWorkAsync(async () =>
            {
                var channel = new SalesChannelEtsy(
                    scope.CompanyId, scope.Code, scope.Code, keystring: "public-client-id", sharedSecret: Secret);
                await _etsyChannels.InsertAsync(channel, autoSave: true);
                return channel.Id;
            });

            var dto = await WithUnitOfWorkAsync(() => _etsyService.GetAsync(id));

            dto.SharedSecret.ShouldBeEmpty();

            // Keystring sır DEĞİL: OAuth client_id'dir ve zaten authorize URL'inde tarayıcıya gider.
            dto.Keystring.ShouldBe("public-client-id");
        }
    }

    private static ChannelScope NewScope()
    {
        var suffix = SimpleGuidGenerator.Instance.Create().ToString("N")[..5].ToUpperInvariant();
        return new ChannelScope(
            SimpleGuidGenerator.Instance.Create(),
            SimpleGuidGenerator.Instance.Create(),
            $"RDC{suffix}");
    }

    private sealed record ChannelScope(Guid TenantId, Guid CompanyId, string Code);
}
