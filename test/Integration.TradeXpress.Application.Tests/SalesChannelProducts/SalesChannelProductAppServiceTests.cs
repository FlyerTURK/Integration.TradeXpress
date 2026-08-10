using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.TrendyolCategories;
using Integration.TradeXpress.TrendyolProducts;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Modularity;
using Xunit;

namespace Integration.TradeXpress.SalesChannelProducts;

/// <summary>
/// <see cref="ISalesChannelProductAppService"/> uçtan uca (DB'li) davranış ağı — BİRLEŞİK kanal-ürün
/// listesinin sözleşmesi.
///
/// <para>Kilitlenenler: ① üç ayrı tablonun TEK listede birleşmesi · ② <b>"senkronize olmuş/olmamış
/// FARKETMEZ"</b> (hiç gönderilmemiş kayıt listede DURUR — ekranın varlık sebebi budur) · ③ nötr durum
/// ÖNCELİĞİ (hata → bekliyor → gönderildi → gönderilmedi) · ④ kanala daraltma · ⑤ öksüz satırın
/// ELENMEMESİ · ⑥ şirket izolasyonu.</para>
///
/// <para><b>③ neden ayrıca çivileniyor:</b> "pazaryerinde canlı AMA son denemesi hata verdi" satırı iki
/// duruma da aday görünür. Kural, listenin işinden çıkar: bu ekran envanter beyanı değil <i>elimi
/// bekleyen iş</i> listesidir → hata kazanır. Kural bozulursa hatalı satır yeşil "Gönderildi" rozetiyle
/// gizlenir ve kimse ona bakmaz — sessiz ve pahalı bir kayıptır.</para>
///
/// <para><b>Ürün KASITEN seedlenmez:</b> ürün kimliği çözülemeyen satırın yine de görünmesi (⑤)
/// dokümante bir karardır — öksüz kanal kaydı gizlenecek değil GÖRÜNECEK bir sorundur.</para>
/// </summary>
public abstract class SalesChannelProductAppServiceTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private static readonly DateTime SyncedAt = new(2026, 8, 9, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime QueuedAt = new(2026, 8, 9, 11, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime FailedAt = new(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);

    private readonly ISalesChannelProductAppService _appService;
    private readonly IRepository<SalesChannelTrN11, Guid> _n11ChannelRepository;
    private readonly IRepository<SalesChannelTrTrendyol, Guid> _trendyolChannelRepository;
    private readonly IRepository<SalesChannelTrN11Product, Guid> _n11ProductRepository;
    private readonly IRepository<SalesChannelTrTrendyolProduct, Guid> _trendyolProductRepository;
    private readonly IRepository<TrendyolCategory, Guid> _trendyolCategoryRepository;
    private readonly ICurrentCompany _currentCompany;

    protected SalesChannelProductAppServiceTests()
    {
        _appService = GetRequiredService<ISalesChannelProductAppService>();
        _n11ChannelRepository = GetRequiredService<IRepository<SalesChannelTrN11, Guid>>();
        _trendyolChannelRepository = GetRequiredService<IRepository<SalesChannelTrTrendyol, Guid>>();
        _n11ProductRepository = GetRequiredService<IRepository<SalesChannelTrN11Product, Guid>>();
        _trendyolProductRepository = GetRequiredService<IRepository<SalesChannelTrTrendyolProduct, Guid>>();
        _trendyolCategoryRepository = GetRequiredService<IRepository<TrendyolCategory, Guid>>();
        _currentCompany = GetRequiredService<ICurrentCompany>();
    }

    // ── ① + ② Birleşme ve "gönderilmemiş de listede" ───────────────────────────────────────────────

    [Fact]
    public async Task GetList_merges_channels_and_keeps_never_pushed_rows()
    {
        // BU TESTİN VARLIK SEBEBİ: ekran "kanala bağladım ama çıkmamış" satırını bulmak için var. Liste
        // yalnız senkron olmuşları gösterseydi aranan satır TAM DA GÖRÜNMEYEN olurdu.
        var companyId = NewId();
        using (_currentCompany.Change(companyId))
        {
            var n11Channel = await SeedN11ChannelAsync(companyId, "MERGE");
            var trendyolChannel = await SeedTrendyolChannelAsync(companyId, "MERGE");

            await SeedN11ProductAsync(companyId, n11Channel.Id, "N11-NEVER");
            await SeedTrendyolProductAsync(companyId, trendyolChannel.Id, "TY-NEVER");

            var result = await _appService.GetListAsync(new SalesChannelProductListRequestDto());

            result.TotalCount.ShouldBe(2);
            result.Items.Select(i => i.ChannelProductCode).ShouldBe(new[] { "N11-NEVER", "TY-NEVER" }, ignoreOrder: true);
            result.Items.ShouldAllBe(i => i.SyncState == ChannelProductSyncState.NotSent);
            result.Items.ShouldAllBe(i => i.RemoteId == null);

            // ⑤ Ürün seedlenmedi → kimlik alanları boş ama satır ELENMEDİ.
            result.Items.ShouldAllBe(i => i.ProductCode == null);
        }
    }

    // ── ③ Durum önceliği ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Failed_wins_over_sent_when_a_live_listing_has_a_last_error()
    {
        var companyId = NewId();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedN11ChannelAsync(companyId, "PREC");

            // Pazaryerinde CANLI (uzak kimlik var) ama son denemesi hata verdi.
            var live = await SeedN11ProductAsync(companyId, channel.Id, "N11-LIVE");
            await MutateN11Async(live, p =>
            {
                p.MarkSynced(987654, "Satışta", "Onaylı", SyncedAt);
                p.MarkSyncFailed("stok güncellenemedi", FailedAt);
            });

            var result = await _appService.GetListAsync(new SalesChannelProductListRequestDto());

            var row = result.Items.ShouldHaveSingleItem();
            row.SyncState.ShouldBe(ChannelProductSyncState.Failed);

            // Canlı olduğu bilgisi KAYBOLMAZ: uzak kimlik aynı satırda durur (iki bilgi birbirinin yerine geçmez).
            row.RemoteId.ShouldBe("987654");
            row.LastError.ShouldBe("stok güncellenemedi");
        }
    }

    [Fact]
    public async Task Queued_push_is_pending_until_it_resolves()
    {
        var companyId = NewId();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedN11ChannelAsync(companyId, "PEND");

            var queued = await SeedN11ProductAsync(companyId, channel.Id, "N11-QUEUED");
            await MutateN11Async(queued, p => p.MarkPushQueued("task-1", QueuedAt));

            var result = await _appService.GetListAsync(new SalesChannelProductListRequestDto());

            result.Items.ShouldHaveSingleItem().SyncState.ShouldBe(ChannelProductSyncState.Pending);
        }
    }

    [Fact]
    public async Task Pushed_listing_without_error_is_sent()
    {
        var companyId = NewId();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedN11ChannelAsync(companyId, "SENT");

            var pushed = await SeedN11ProductAsync(companyId, channel.Id, "N11-SENT");
            await MutateN11Async(pushed, p => p.MarkSynced(1234, "Satışta", "Onaylı", SyncedAt));

            var result = await _appService.GetListAsync(new SalesChannelProductListRequestDto());

            var row = result.Items.ShouldHaveSingleItem();
            row.SyncState.ShouldBe(ChannelProductSyncState.Sent);
            row.RemoteId.ShouldBe("1234");

            // N11'in İKİ durumu da taşınır — "satışta ama onay bekliyor" gerçek bir durumdur.
            row.RemoteStatus.ShouldBe("Satışta / Onaylı");
        }
    }

    [Fact]
    public async Task An_imported_listing_we_never_pushed_is_imported_not_sent()
    {
        // ⑦ CANLIDA YAŞANAN YALAN (2026-08-10): mağaza içe aktarımı uzak kimliği doldurunca liste
        // "Gönderildi" diyordu — oysa BİZ hiçbir şey göndermemiştik. Hakan haklı olarak sordu:
        // "Senkron Gönderildi diyor. NEYİ gönderdi?". Rozet bir DELİL beyanıdır; delil yoksa
        // beyan da olmamalı. "Gönderildi" artık YALNIZ bizim push'umuzun izinden (LastSyncedAt)
        // türer; uzak kimliğin tek başına anlattığı şey "orada bir liste VAR" = İçe Aktarıldı.
        //
        // Bu ayrım kaybolursa hata SESSİZDİR: satır yeşil rozetle listenin dibine düşer, kimse
        // ona bakmaz ve "gönderdim sanıyordum" ile biter — düzeltilen tam olarak buydu.
        var companyId = NewId();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedTrendyolChannelAsync(companyId, "IMPORT");

            var imported = await SeedTrendyolProductAsync(companyId, channel.Id, "TY-IMPORTED");
            await MutateTrendyolAsync(imported, p =>
                p.ApplyRemoteSnapshot("TY-MAIN-77", approved: true, onSale: true, listPrice: 1250.50m));

            var result = await _appService.GetListAsync(new SalesChannelProductListRequestDto());

            var row = result.Items.ShouldHaveSingleItem();
            row.SyncState.ShouldBe(ChannelProductSyncState.Imported);

            // Uzak kimlik ve pazaryeri görüntüsü KAYBOLMAZ — durum değişti, bilgi değil.
            row.RemoteId.ShouldBe("TY-MAIN-77");
            row.RemotePrice.ShouldBe(1250.50m);
            row.RemoteOnSale.ShouldBe(true);
            row.LastSyncedAt.ShouldBeNull();
        }
    }

    [Fact]
    public async Task Pushing_an_imported_listing_promotes_it_to_sent()
    {
        // İki durumun SINIRI: aynı satır bir kez de olsa bizim elimizden geçince artık "İçe
        // Aktarıldı" değildir. Üstteki test tek başına, "Imported'ı her zaman döndür" gibi bir
        // gevşetmeyle de geçerdi; bu test o kaçışı kapatır.
        var companyId = NewId();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedTrendyolChannelAsync(companyId, "PROMOTE");

            var imported = await SeedTrendyolProductAsync(companyId, channel.Id, "TY-PROMOTED");
            await MutateTrendyolAsync(imported, p =>
            {
                p.ApplyRemoteSnapshot("TY-MAIN-88", approved: true, onSale: true, listPrice: 990m);
                p.MarkSubmitted("batch-1", "UpdatePriceAndInventory", SyncedAt);
            });

            var result = await _appService.GetListAsync(new SalesChannelProductListRequestDto());

            result.Items.ShouldHaveSingleItem().SyncState.ShouldBe(ChannelProductSyncState.Sent);
        }
    }

    // ── ⑨ Kanaldaki son fiyat/adet ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Channel_price_and_quantity_come_from_what_actually_reached_the_channel()
    {
        // "Satış kanalında olan son fiyat ve stok bilgisini her türlü görmem lazım" (2026-08-10 Hakan).
        // Kaynak SKU'ların LastSent* değerleridir — bunlar YALNIZ başarılı gönderimde terfi eder, yani
        // "gönderdiğimizi sandığımız" değil karşı tarafın kabul ettiği değerdir.
        //
        // ADET TOPLANIR (kanalda görünen toplam stok), FİYAT ARALIĞA DÖNER: varyantlar farklı fiyattaysa
        // tek sayı göstermek diğerlerini gizlerdi.
        var companyId = NewId();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedN11ChannelAsync(companyId, "LASTSENT");
            var product = await SeedN11ProductAsync(companyId, channel.Id, "N11-LASTSENT");

            await MutateN11Async(product, p =>
            {
                p.UpsertImportedSku(NewId(), "SKU-A", n11SkuId: 4);
                p.UpsertImportedSku(NewId(), "SKU-B", n11SkuId: 5);

                // BAŞARILI push'un kaydı — LastSent*'i yalnız bu yol ilerletir.
                p.RecordStockPriceSync("SKU-A", quantity: 3, optionPrice: 1200m, version: 1);
                p.RecordStockPriceSync("SKU-B", quantity: 7, optionPrice: 1500m, version: 1);
            });

            var result = await _appService.GetListAsync(new SalesChannelProductListRequestDto());

            var row = result.Items.ShouldHaveSingleItem();
            row.ChannelPrice.ShouldBe(1200m);
            row.ChannelPriceMax.ShouldBe(1500m);
            row.ChannelQuantity.ShouldBe(10);
        }
    }

    [Fact]
    public async Task Channel_price_and_quantity_are_null_when_nothing_ever_reached_the_channel()
    {
        // BOŞLUK ile SIFIR AYRI ŞEYLERDİR: "hiç göndermedik" bilgisizliktir, "0 adet" ise bir BEYANDIR
        // ("tükendi"). Bilgisizliği 0 diye göstermek, ekranda tükenmiş bir ürün varmış gibi okunurdu.
        var companyId = NewId();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedN11ChannelAsync(companyId, "NEVERSENT");
            await SeedN11ProductAsync(companyId, channel.Id, "N11-NEVERSENT");

            var result = await _appService.GetListAsync(new SalesChannelProductListRequestDto());

            var row = result.Items.ShouldHaveSingleItem();
            row.ChannelPrice.ShouldBeNull();
            row.ChannelQuantity.ShouldBeNull();
        }
    }

    // ── ⑧ Kategori: kökten tam yol ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Category_is_shown_as_the_full_path_from_the_root()
    {
        // Kanal kaydı yalnız YAPRAĞIN adını dondurur ve yaprak adları ağaç içinde benzersiz DEĞİLDİR
        // ("Bileklik" hem takıda hem saat aksesuarında geçer). Komisyon oranı ve zorunlu öznitelikler
        // dala bağlı olduğundan, yaprak adı hangi dalda olunduğunu SÖYLEMEZ — yol söyler.
        var companyId = NewId();
        using (_currentCompany.Change(companyId))
        {
            await SeedTrendyolCategoryAsync("ty-root", null, "Kozmetik");
            await SeedTrendyolCategoryAsync("ty-mid", "ty-root", "Cilt Bakımı");
            await SeedTrendyolCategoryAsync("ty-leaf", "ty-mid", "Göz Makyaj Temizleyici");

            var channel = await SeedTrendyolChannelAsync(companyId, "PATH");
            var product = await SeedTrendyolProductAsync(companyId, channel.Id, "TY-PATH");
            await MutateTrendyolAsync(product, p => p.SetCategory("ty-leaf", "Göz Makyaj Temizleyici"));

            var result = await _appService.GetListAsync(new SalesChannelProductListRequestDto());

            result.Items.ShouldHaveSingleItem().CategoryName
                .ShouldBe("Kozmetik > Cilt Bakımı > Göz Makyaj Temizleyici");
        }
    }

    [Fact]
    public async Task A_category_missing_from_the_tree_falls_back_to_the_stored_leaf_name()
    {
        // Pazaryeri kategoriyi kaldırmış/yeniden numaralandırmış olabilir. O durumda hücreyi BOŞALTMAK,
        // elde duran doğru bilgiyi silmek olurdu — eksik bilgi göstermek, hiç göstermemekten iyidir.
        var companyId = NewId();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedTrendyolChannelAsync(companyId, "STALE");
            var product = await SeedTrendyolProductAsync(companyId, channel.Id, "TY-STALE");
            await MutateTrendyolAsync(product, p => p.SetCategory("ty-vanished", "Kaldırılmış Kategori"));

            var result = await _appService.GetListAsync(new SalesChannelProductListRequestDto());

            result.Items.ShouldHaveSingleItem().CategoryName.ShouldBe("Kaldırılmış Kategori");
        }
    }

    // ── ④ Kanala daraltma (kanal edit formunun tek farkı) ──────────────────────────────────────────

    [Fact]
    public async Task Narrowing_to_a_channel_excludes_other_channels()
    {
        var companyId = NewId();
        using (_currentCompany.Change(companyId))
        {
            var n11Channel = await SeedN11ChannelAsync(companyId, "NARROW");
            var trendyolChannel = await SeedTrendyolChannelAsync(companyId, "NARROW");

            await SeedN11ProductAsync(companyId, n11Channel.Id, "N11-ONLY");
            await SeedTrendyolProductAsync(companyId, trendyolChannel.Id, "TY-OTHER");

            var result = await _appService.GetListAsync(new SalesChannelProductListRequestDto
            {
                SalesChannelId = n11Channel.Id,
            });

            result.TotalCount.ShouldBe(1);
            result.Items.ShouldHaveSingleItem().ChannelProductCode.ShouldBe("N11-ONLY");
        }
    }

    [Fact]
    public async Task Sync_state_filter_narrows_to_the_requested_state()
    {
        var companyId = NewId();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedN11ChannelAsync(companyId, "FILTER");

            await SeedN11ProductAsync(companyId, channel.Id, "N11-FRESH");
            var pushed = await SeedN11ProductAsync(companyId, channel.Id, "N11-DONE");
            await MutateN11Async(pushed, p => p.MarkSynced(555, "Satışta", null, SyncedAt));

            var result = await _appService.GetListAsync(new SalesChannelProductListRequestDto
            {
                SyncState = ChannelProductSyncState.NotSent,
            });

            result.TotalCount.ShouldBe(1);
            result.Items.ShouldHaveSingleItem().ChannelProductCode.ShouldBe("N11-FRESH");
        }
    }

    // ── ⑥ Şirket izolasyonu ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rows_of_another_company_are_not_listed()
    {
        var ownCompanyId = NewId();
        var foreignCompanyId = NewId();

        Guid foreignChannelId;
        using (_currentCompany.Change(foreignCompanyId))
        {
            var foreignChannel = await SeedN11ChannelAsync(foreignCompanyId, "FOREIGN");
            foreignChannelId = foreignChannel.Id;
            await SeedN11ProductAsync(foreignCompanyId, foreignChannelId, "N11-FOREIGN");
        }

        using (_currentCompany.Change(ownCompanyId))
        {
            var channel = await SeedN11ChannelAsync(ownCompanyId, "OWN");
            await SeedN11ProductAsync(ownCompanyId, channel.Id, "N11-OWN");

            var result = await _appService.GetListAsync(new SalesChannelProductListRequestDto());

            result.TotalCount.ShouldBe(1);
            result.Items.ShouldHaveSingleItem().ChannelProductCode.ShouldBe("N11-OWN");

            // Yabancı kanala AÇIKÇA daraltmak da sızıntı açmaz (kapsam sunucuda, istekte değil).
            var targeted = await _appService.GetListAsync(new SalesChannelProductListRequestDto
            {
                SalesChannelId = foreignChannelId,
            });

            targeted.TotalCount.ShouldBe(0);
        }
    }

    // ── Kurulum yardımcıları ───────────────────────────────────────────────────────────────────────

    private async Task<SalesChannelTrN11> SeedN11ChannelAsync(Guid companyId, string suffix)
    {
        return await WithUnitOfWorkAsync(async () =>
            await _n11ChannelRepository.InsertAsync(
                new SalesChannelTrN11(companyId, $"N11-{suffix}", $"N11 Kanal {suffix}", "app-key", "app-secret"),
                autoSave: true));
    }

    private async Task<SalesChannelTrTrendyol> SeedTrendyolChannelAsync(Guid companyId, string suffix)
    {
        return await WithUnitOfWorkAsync(async () =>
            await _trendyolChannelRepository.InsertAsync(
                new SalesChannelTrTrendyol(companyId, $"TY-{suffix}", $"Trendyol Kanal {suffix}", "seller-1", "api-key", "api-secret"),
                autoSave: true));
    }

    private async Task<Guid> SeedN11ProductAsync(Guid companyId, Guid salesChannelId, string sellerCode)
    {
        var entity = await WithUnitOfWorkAsync(async () =>
            await _n11ProductRepository.InsertAsync(
                new SalesChannelTrN11Product(
                    companyId,
                    salesChannelId,
                    NewId(),
                    sellerCode,
                    1,
                    "cat-1",
                    "Standart Kargo"),
                autoSave: true));

        return entity.Id;
    }

    private async Task<Guid> SeedTrendyolProductAsync(Guid companyId, Guid salesChannelId, string productMainId)
    {
        var entity = await WithUnitOfWorkAsync(async () =>
            await _trendyolProductRepository.InsertAsync(
                new SalesChannelTrTrendyolProduct(
                    companyId,
                    salesChannelId,
                    NewId(),
                    productMainId,
                    1,
                    "cat-1",
                    "brand-1"),
                autoSave: true));

        return entity.Id;
    }

    /// <summary>Push SONUCUNU elde kurar — bu testler senkron durumunun OKUNMASINI çiviler, pazaryerine
    /// giden yolu değil (o yol kanalın kendi servisinin testlerinde).</summary>
    private Task MutateN11Async(Guid id, Action<SalesChannelTrN11Product> mutate)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var entity = await _n11ProductRepository.GetAsync(id);
            mutate(entity);
            await _n11ProductRepository.UpdateAsync(entity, autoSave: true);
        });
    }

    /// <summary>Kategori ağacı HOST-GLOBAL'dir (<c>IMultiTenant</c> DEĞİL, TenantId kolonu YOK) → tenant
    /// değiştirmeye gerek yok; şirket kapsamının da dışındadır.</summary>
    private async Task SeedTrendyolCategoryAsync(string externalId, string? parentExternalId, string name)
    {
        await WithUnitOfWorkAsync(async () =>
            await _trendyolCategoryRepository.InsertAsync(
                new TrendyolCategory(externalId, parentExternalId, name, isLeaf: parentExternalId != null),
                autoSave: true));
    }

    private Task MutateTrendyolAsync(Guid id, Action<SalesChannelTrTrendyolProduct> mutate)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var entity = await _trendyolProductRepository.GetAsync(id);
            mutate(entity);
            await _trendyolProductRepository.UpdateAsync(entity, autoSave: true);
        });
    }

    /// <summary>Kalıcı olmayan test kimliği — <c>Guid.NewGuid()</c> yasak (CLAUDE.md §8), DI'sız bağlamda
    /// ABP'nin basit üreteci kullanılır (emsal: <c>ChannelQuestionAppServiceTests</c>).</summary>
    private static Guid NewId()
    {
        return SimpleGuidGenerator.Instance.Create();
    }
}
