using System;
using System.Threading.Tasks;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.SalesChannels;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.MultiCompany;

/// <summary>
/// SATIŞ KANALI TEKİL ERİŞİMİNİN ŞİRKET SINIRI — <b>derinlemesine savunma</b> ağı.
///
/// <para><b>Kapatılan açık:</b> kanal servislerinin Get/Update/Delete uçları çıplak <c>_repository.GetAsync(id)</c>
/// çağırıyordu ve güvenliği tamamen global query-filter'a yaslıyordu. O savunmanın tek bir ön koşulu vardı:
/// <c>ICurrentCompany.Id</c> DOLU olmalı. HTTP API'de (:44388) şirket bağlamı hiç kurulmadığından değer
/// <c>null</c> kalıyor, filtre PERMISSIVE (konsolide) kola düşüyor ve koruma <b>sessizce</b> yok oluyordu —
/// tenant içindeki her şirketin kanalı okunabilir, güncellenebilir ve silinebilirdi.</para>
///
/// <para>Bu testler iki bağımsız kapıyı da sürer: bağlam YOKSA açık hata (filtreye düşme), bağlam VARSA
/// yabancı şirketin kaydı <see cref="EntityNotFoundException"/> (var olmayan kayıtla aynı cevap — yabancı
/// kaydın VARLIĞI da sızmaz).</para>
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class SalesChannelCompanyBoundaryTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly ISalesChannelTrN11AppService _n11Service;
    private readonly ISalesChannelAppService _channelService;
    private readonly IRepository<SalesChannelTrN11, Guid> _channels;
    private readonly TestCompanyContextProvider _companyContext;
    private readonly ICurrentTenant _currentTenant;

    public SalesChannelCompanyBoundaryTests()
    {
        _n11Service     = GetRequiredService<ISalesChannelTrN11AppService>();
        _channelService = GetRequiredService<ISalesChannelAppService>();
        _channels       = GetRequiredService<IRepository<SalesChannelTrN11, Guid>>();
        _companyContext = GetRequiredService<TestCompanyContextProvider>();
        _currentTenant  = GetRequiredService<ICurrentTenant>();
    }

    /// <summary>Kardeş şirketin kanalı okunamaz/güncellenemez/silinemez — kaydın VARLIĞI da sızmaz.
    /// <para><b>Not:</b> bu vaka global query-filter tarafından ZATEN kapatılıyordu (şirket bağlamı dolu olduğu
    /// sürece). Buradaki değeri, açık koşulun filtreyle AYNI cevabı vermesini pinlemek — iki savunmanın
    /// birbirinden ayrışması, aşağıdaki bağlamsız vakalar kadar sessiz bir tuzaktır.</para></summary>
    [Fact]
    public async Task Sibling_company_channel_is_not_reachable_by_id()
    {
        var scenario = await SeedSiblingChannelAsync();

        using (_currentTenant.Change(scenario.TenantId))
        {
            _companyContext.CompanyId = scenario.SiblingCompanyId;

            await Should.ThrowAsync<EntityNotFoundException>(
                () => WithUnitOfWorkAsync(() => _n11Service.GetAsync(scenario.ChannelId)));

            await Should.ThrowAsync<EntityNotFoundException>(
                () => WithUnitOfWorkAsync(() => _n11Service.DeleteAsync(scenario.ChannelId)));

            await Should.ThrowAsync<EntityNotFoundException>(
                () => WithUnitOfWorkAsync(() => _channelService.DeleteAsync(scenario.ChannelId)));

            // Sahibi hâlâ okuyabiliyor — kapı yalnız YABANCIYA kapalı, kanal silinmedi.
            _companyContext.CompanyId = scenario.OwnerCompanyId;
            (await WithUnitOfWorkAsync(() => _n11Service.GetAsync(scenario.ChannelId)))
                .Id.ShouldBe(scenario.ChannelId);
        }
    }

    /// <summary>ŞİRKET BAĞLAMI YOKKEN (API'nin eski hâli) tekil erişim <b>açık hataya</b> düşer — filtreye değil.
    /// <para>Eskiden bu bağlamda filtre permissive kola düşüyor ve çağrı BAŞARIYLA yabancı kaydı döndürüyordu.
    /// Sessiz başarı, sessiz açıktır: hiçbir log, hiçbir istisna, yalnız görülmemesi gereken veri. Bu testin
    /// ASIL kapattığı delik budur (yukarıdaki kardeş vakası zaten kapalıydı).</para></summary>
    [Fact]
    public async Task Missing_company_context_is_rejected_instead_of_falling_back_to_consolidated_read()
    {
        var scenario = await SeedSiblingChannelAsync();

        using (_currentTenant.Change(scenario.TenantId))
        {
            _companyContext.CompanyId = null;

            (await Should.ThrowAsync<BusinessException>(
                () => WithUnitOfWorkAsync(() => _n11Service.GetAsync(scenario.ChannelId))))
                .Code.ShouldBe("TradeXpress:MultiCompany:WorkingCompanyRequired");

            (await Should.ThrowAsync<BusinessException>(
                () => WithUnitOfWorkAsync(() => _channelService.DeleteAsync(scenario.ChannelId))))
                .Code.ShouldBe("TradeXpress:MultiCompany:WorkingCompanyRequired");
        }
    }

    /// <summary>SENTINEL bağlam (<see cref="Guid.Empty"/> — HTTP API'nin yeni varsayılanı, "erişim yok")
    /// hiçbir şirket kaydına ulaşamaz.</summary>
    [Fact]
    public async Task Sentinel_company_context_reaches_no_owned_channel()
    {
        var scenario = await SeedSiblingChannelAsync();

        using (_currentTenant.Change(scenario.TenantId))
        {
            _companyContext.CompanyId = Guid.Empty;

            // Sentinel "şirket yok" demektir → guard bağlam yokmuş gibi reddeder (fail-closed).
            (await Should.ThrowAsync<BusinessException>(
                () => WithUnitOfWorkAsync(() => _n11Service.GetAsync(scenario.ChannelId))))
                .Code.ShouldBe("TradeXpress:MultiCompany:WorkingCompanyRequired");
        }
    }

    // ── fixture ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>TENANT altında iki şirket + BİRİNCİSİNE ait bir N11 kanalı.
    /// <para><b>Tenant ŞART:</b> host kaydında (TenantId=null) şirket filtresinin host-muafiyet kolu devreye
    /// girer ve kayıt herkese görünür olur — üretimde kanallar daima bir tenant'a aittir, test de o şekli
    /// sürmelidir. Tenant'sız kurulmuş bir test, filtrenin gerçekte ne kadar koruduğunu YANLIŞ gösterirdi.</para>
    /// <para>Kanal doğrudan depoya yazılır: amaç kurulum kurallarını değil sınır davranışını sürmek.</para></summary>
    private async Task<ChannelBoundaryScenario> SeedSiblingChannelAsync()
    {
        var suffix           = SimpleGuidGenerator.Instance.Create().ToString("N")[..6].ToUpperInvariant();
        var tenantId         = SimpleGuidGenerator.Instance.Create();
        var ownerCompanyId   = SimpleGuidGenerator.Instance.Create();
        var siblingCompanyId = SimpleGuidGenerator.Instance.Create();

        using (_currentTenant.Change(tenantId))
        {
            _companyContext.CompanyId = ownerCompanyId;

            var channelId = await WithUnitOfWorkAsync(async () =>
            {
                var channel = new SalesChannelTrN11(
                    ownerCompanyId, $"SCB{suffix}", $"Kanal {suffix}", "key", "secret");
                await _channels.InsertAsync(channel, autoSave: true);
                return channel.Id;
            });

            return new ChannelBoundaryScenario(tenantId, ownerCompanyId, siblingCompanyId, channelId);
        }
    }

    private sealed record ChannelBoundaryScenario(
        Guid TenantId, Guid OwnerCompanyId, Guid SiblingCompanyId, Guid ChannelId);
}
