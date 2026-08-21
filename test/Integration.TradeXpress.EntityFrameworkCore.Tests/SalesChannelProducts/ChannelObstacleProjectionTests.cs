using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.TrendyolProducts;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.SalesChannelProducts;

/// <summary>
/// KANAL ÜRÜNLERİ LİSTESİ, ENGEL ALANLARIYLA BİRLİKTE GERÇEKTEN SORGULANABİLİYOR MU.
///
/// <para><b>Neden veritabanına giden bir test:</b> SKU satırları <c>OwnsMany(...).ToJson()</c> ile TEK bir
/// JSON kolonunda yaşıyor. Bu kolon üzerindeki her LINQ işlecinin SQL'e çevrilebileceği GARANTİ DEĞİLDİR —
/// çevrilemeyen bir ifade derlemede sessiz kalır, testlerden geçer ve yalnız kullanıcı listeyi açtığında
/// patlar. Engel sütunları <c>Where(...).Select(...).FirstOrDefault()</c> ve <c>Max(...)</c> kullanıyor;
/// bu test tam olarak o çevirinin çalıştığını kanıtlar.</para>
///
/// <para><b>Ne pinlenir:</b> ① sorgu çalışır (çeviri hatası yok) ② engel EN AĞIR olandan seçilir
/// ③ gerekçe engeli TAŞIYAN kalemden gelir ④ engelsiz kayıt engelli görünmez.</para>
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class ChannelObstacleProjectionTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly ISalesChannelProductAppService _service;
    private readonly IRepository<SalesChannelTrTrendyolProduct, Guid> _products;
    private readonly TestCompanyContextProvider _companyContext;
    private readonly ICurrentTenant _currentTenant;

    public ChannelObstacleProjectionTests()
    {
        _service = GetRequiredService<ISalesChannelProductAppService>();
        _products = GetRequiredService<IRepository<SalesChannelTrTrendyolProduct, Guid>>();
        _companyContext = GetRequiredService<TestCompanyContextProvider>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
    }

    [Fact]
    public async Task The_obstacle_columns_are_translatable_and_report_the_heaviest_obstacle()
    {
        var companyId = Guid.NewGuid();
        _companyContext.CompanyId = companyId;

        using (_currentTenant.Change(null))
        {
            var blocked = NewProduct(companyId, "ENGELLI");
            // İKİ kalem: biri temiz, biri karalistede + kilitli. Kayıt "engelsiz" görünemez.
            blocked.UpsertImportedSku(Guid.NewGuid(), "BR-CLEAN", "STK-C", null,
                new TrendyolRemoteListingState(Quantity: 4));
            blocked.UpsertImportedSku(Guid.NewGuid(), "BR-BLACK", "STK-B", null,
                new TrendyolRemoteListingState(
                    Blacklisted: true, BlacklistReason: "Belge eksik",
                    Locked: true, LockReason: "UNSUPPLIED_PRODUCT",
                    HasActiveCampaign: true,
                    ProductUrl: "https://www.trendyol.com/x-p-1",
                    UpdatedAtUtc: new DateTime(2026, 3, 4, 8, 0, 0, DateTimeKind.Utc)));
            await _products.InsertAsync(blocked, autoSave: true);

            var clean = NewProduct(companyId, "TEMIZ");
            clean.UpsertImportedSku(Guid.NewGuid(), "BR-OK", "STK-OK", null,
                new TrendyolRemoteListingState(Quantity: 9));
            await _products.InsertAsync(clean, autoSave: true);

            // HİÇ SKU'suz kayıt: boş JSON koleksiyonu üzerindeki FirstOrDefault/Max çevirisi de çalışmalı —
            // canlıda SKU'ları henüz işlenmemiş (ör. push öncesi elle açılmış) kayıt meşru bir hâldir.
            await _products.InsertAsync(NewProduct(companyId, "BOSSKU"), autoSave: true);

            // İkinci ağırlık çifti: red + arşiv birlikteyken RED kazanmalı — sıralama yalnız
            // karaliste/kilit çiftiyle değil zincirin alt ucunda da pinli kalsın.
            var rejected = NewProduct(companyId, "REDLI");
            rejected.UpsertImportedSku(Guid.NewGuid(), "BR-REJ", "STK-R", null,
                new TrendyolRemoteListingState(
                    Rejected: true, RejectReason: "Görsel uygunsuz",
                    Archived: true));
            await _products.InsertAsync(rejected, autoSave: true);
        }

        var rows = await _service.GetListAsync(new SalesChannelProductListRequestDto { MaxResultCount = 100 });

        var blockedRow = rows.Items.Single(r => r.ChannelProductCode!.StartsWith("ENGELLI", StringComparison.Ordinal));
        blockedRow.Obstacle.ShouldBe(ChannelListingObstacle.Blacklisted);   // kilitten AĞIR olan kazanır
        blockedRow.ObstacleReason.ShouldBe("Belge eksik");                  // gerekçe ENGELLİ kalemden
        blockedRow.HasActiveCampaign.ShouldBe(true);
        blockedRow.RemoteUrl.ShouldBe("https://www.trendyol.com/x-p-1");
        blockedRow.RemoteUpdatedAt.ShouldNotBeNull();

        var cleanRow = rows.Items.Single(r => r.ChannelProductCode!.StartsWith("TEMIZ", StringComparison.Ordinal));
        cleanRow.Obstacle.ShouldBe(ChannelListingObstacle.None);
        cleanRow.ObstacleReason.ShouldBeNull();
        // Pazaryeri hiç bildirmediyse "kampanya yok" DEĞİL "bilinmiyor" denir.
        cleanRow.HasActiveCampaign.ShouldBeNull();

        // SKU'suz kayıt: sorgu düşmez, engel "bilinmiyor/yok" olarak döner, RemoteUpdatedAt boş kalır.
        var emptyRow = rows.Items.Single(r => r.ChannelProductCode!.StartsWith("BOSSKU", StringComparison.Ordinal));
        emptyRow.Obstacle.ShouldBe(ChannelListingObstacle.None);
        emptyRow.ObstacleReason.ShouldBeNull();
        emptyRow.HasActiveCampaign.ShouldBeNull();
        emptyRow.RemoteUpdatedAt.ShouldBeNull();

        var rejectedRow = rows.Items.Single(r => r.ChannelProductCode!.StartsWith("REDLI", StringComparison.Ordinal));
        rejectedRow.Obstacle.ShouldBe(ChannelListingObstacle.Rejected);     // arşivden AĞIR olan kazanır
        rejectedRow.ObstacleReason.ShouldBe("Görsel uygunsuz");
    }

    private static SalesChannelTrTrendyolProduct NewProduct(Guid companyId, string prefix)
    {
        return new SalesChannelTrTrendyolProduct(
            companyId: companyId,
            salesChannelId: Guid.NewGuid(),
            productId: Guid.NewGuid(),
            productMainId: $"{prefix}-{Guid.NewGuid():N}"[..20],
            sequenceNo: 1,
            categoryId: "411",
            brandId: "82");
    }
}
