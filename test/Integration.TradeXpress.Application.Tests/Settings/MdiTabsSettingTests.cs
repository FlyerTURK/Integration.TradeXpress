using System;
using System.Threading.Tasks;
using Shouldly;
using Volo.Abp.Modularity;
using Xunit;

namespace Integration.TradeXpress.Settings;

/// <summary>MDI sekme kalıcılığının sunucu-tarafı anahtar çözümlemesi + legacy anahtar göçü.
/// Anahtar SUNUCUDA working-branch ayarından kurulur (client yükleme sırasından bağımsız);
/// eski client-side ham anahtar ("MdiTabs") verisi yeni anahtara köprülenir (çift-yazım, rollback güvenliği).</summary>
public abstract class MdiTabsSettingTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IUserUiSettingAppService _appService;

    protected MdiTabsSettingTests()
    {
        _appService = GetRequiredService<IUserUiSettingAppService>();
    }

    [Fact]
    public async Task Get_returns_empty_array_when_nothing_stored()
    {
        (await _appService.GetMdiTabsAsync()).ShouldBe("[]");
    }

    [Fact]
    public async Task Set_then_get_round_trips()
    {
        const string json = """{"Tabs":[{"Url":"/accounts","Title":"Hesaplar"}],"ActiveUrl":"/accounts"}""";
        await _appService.SetMdiTabsAsync(json);
        (await _appService.GetMdiTabsAsync()).ShouldBe(json);
    }

    [Fact]
    public async Task Legacy_raw_key_is_read_when_new_key_is_empty()
    {
        // Eski TabManager doğrudan ham "MdiTabs" anahtarına yazıyordu — göç köprüsü mevcut kullanıcı
        // verisini kaybetmeden devralmalı.
        const string legacyJson = """{"Tabs":[{"Url":"/companies","Title":"Şirketler"}],"ActiveUrl":null}""";
        await _appService.SetGridStateAsync("MdiTabs", legacyJson);

        (await _appService.GetMdiTabsAsync()).ShouldBe(legacyJson);
    }

    [Fact]
    public async Task New_key_wins_over_stale_legacy_key()
    {
        const string current = """{"Tabs":[],"ActiveUrl":null,"Marker":"new"}""";
        await _appService.SetMdiTabsAsync(current);

        // Legacy anahtar sonradan farklılaşsa bile (rollback'te eski client yazdı senaryosu) yeni anahtar kazanır.
        await _appService.SetGridStateAsync("MdiTabs", """{"Marker":"stale-legacy"}""");

        (await _appService.GetMdiTabsAsync()).ShouldBe(current);
    }

    [Fact]
    public async Task Key_is_scoped_by_working_branch()
    {
        var branchId = Guid.NewGuid().ToString();

        await _appService.SetMdiTabsAsync("""{"Marker":"branchless"}""");

        await _appService.SetWorkingBranchAsync(branchId);
        (await _appService.GetMdiTabsAsync()).ShouldBe("[]");   // şube kovası ayrı — şubesiz veri sızmaz

        await _appService.SetMdiTabsAsync("""{"Marker":"branch"}""");
        (await _appService.GetMdiTabsAsync()).ShouldBe("""{"Marker":"branch"}""");

        await _appService.SetWorkingBranchAsync(null);
        (await _appService.GetMdiTabsAsync()).ShouldBe("""{"Marker":"branchless"}""");
    }
}
