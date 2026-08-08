using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.SalesChannels.Etsy;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Volo.Abp.Caching;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Volo.Abp.Uow;
using Xunit;

namespace Integration.TradeXpress.SalesChannels;

/// <summary>
/// Etsy OAuth state kaydının BAĞLAM taşıması — <c>ChannelId</c> + <c>TenantId</c> + <b><c>CompanyId</c></b>.
///
/// <para><b>Neden pinli:</b> callback isteğinin kimliği yoktur (Etsy tarayıcıyı yönlendirir), dolayısıyla
/// working context de yoktur. Şirket bağlamı state kaydında taşınmazsa iki kötü sonuçtan biri doğar: bağlam
/// sentinel kalır ve kanal GÖRÜNMEZ (token hiç yazılamaz, bağlantı sessizce başarısız olur), ya da bağlam
/// null kalır ve şirket filtresi permissive kola düşer — o zaman callback tenant'taki HERHANGİ bir şirketin
/// kanalına yazabilir hâle gelir. Alan silinirse bu test kırılır.</para>
///
/// <para>Şirket ambient bağlamdan DEĞİL kanalın kendisinden alınır: akış tek bir kanala bağlıdır ve o kanalın
/// sahibi tek doğru cevaptır.</para>
/// </summary>
public class EtsyOAuthStateContextTests
{
    [Fact]
    public async Task Start_stores_channel_owner_company_in_the_state_item()
    {
        var ownerCompanyId = SimpleGuidGenerator.Instance.Create();
        var tenantId = SimpleGuidGenerator.Instance.Create();
        var channel = new SalesChannelEtsy(ownerCompanyId, "ETSY", "Etsy Kanalı", "key-1", "secret-1");

        EtsyOAuthStateCacheItem? stored = null;
        var cache = Substitute.For<IDistributedCache<EtsyOAuthStateCacheItem>>();
        cache
            .SetAsync(
                Arg.Any<string>(), Arg.Do<EtsyOAuthStateCacheItem>(x => stored = x),
                Arg.Any<DistributedCacheEntryOptions>(), Arg.Any<bool?>(), Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var currentTenant = Substitute.For<ICurrentTenant>();
        currentTenant.Id.Returns(tenantId);

        var service = new EtsyOAuthService(
            Substitute.For<IRepository<SalesChannelEtsy, Guid>>(),
            Substitute.For<IEtsyOAuthClient>(),
            cache,
            BuildConfiguration(),
            currentTenant,
            Substitute.For<ICurrentCompany>(),      // ambient BOŞ — değer kanaldan gelmeli
            Substitute.For<IUnitOfWorkManager>(),
            Substitute.For<IClock>(),
            NullLogger<EtsyOAuthService>.Instance);

        var url = await service.StartAsync(channel);

        stored.ShouldNotBeNull();
        stored!.ChannelId.ShouldBe(channel.Id);
        stored.TenantId.ShouldBe(tenantId);
        stored.CompanyId.ShouldBe(ownerCompanyId);
        stored.CodeVerifier.ShouldNotBeNullOrWhiteSpace();

        url.ShouldContain("code_challenge_method=S256");
    }

    private static IConfiguration BuildConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:SelfUrl"] = "https://example.invalid",
            })
            .Build();
    }
}
