using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bunit;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Blazor.Client.Pages.SalesChannelProducts;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.TrendyolProducts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Blazor.Tests.Pages;

/// <summary>
/// Kanal-aksiyon bileşeninin (<see cref="ChannelProductActions"/>) davranış sözleşmesi — üç yer (ürünün satışa hazırlık paneli ·
/// ürün formu kanal sekmesi · kanal ürünleri listesi) aynı bileşeni çizdiği için burada bir kez kilitlenir:
/// (1) Trendyol gönderimi ÖNCE insan onayı ister, "Hayır" → servis HİÇ çağrılmaz; (2) "Evet" → gönderim bir kez
/// gider ve sahip <c>OnChanged</c> ile haberdar edilir; (3) N11 gönderimi onaysızdır; (4) başarılı aksiyonun
/// <c>SyncWarnings</c>'i her biri ayrı uyarı toast'ı olur (Trendyol'da bu uyarılar eskiden hiç gösterilmiyordu);
/// (5) kaydedilmemiş satırda / Etsy'de düğme yerine metin çizilir.
/// </summary>
public class ChannelProductActionsTests : BlazorComponentTestBase
{
    private readonly ISalesChannelTrN11ProductAppService _n11;
    private readonly ISalesChannelTrTrendyolProductAppService _trendyol;
    private readonly IUiInteractionService _ui;

    public ChannelProductActionsTests()
    {
        _n11 = AddSubstitute<ISalesChannelTrN11ProductAppService>();
        _trendyol = AddSubstitute<ISalesChannelTrTrendyolProductAppService>();
        // Taban sınıf onay/toast sahtesini zaten kaydetti; kayıt örneğine koleksiyondan ulaşılır (sağlayıcıyı
        // erken kurmamak için GetRequiredService ÇAĞRILMAZ — bUnit sağlayıcı kurulduktan sonra kayıt kabul etmez).
        _ui = (IUiInteractionService)Services.Single(d => d.ServiceType == typeof(IUiInteractionService)).ImplementationInstance!;

        _n11.PushToN11Async(Arg.Any<Guid>()).Returns(call => Task.FromResult(new SalesChannelTrN11ProductDto { Id = call.Arg<Guid>() }));
        _trendyol.PushToTrendyolAsync(Arg.Any<Guid>()).Returns(call => Task.FromResult(new SalesChannelTrTrendyolProductDto { Id = call.Arg<Guid>() }));
    }

    [Fact]
    public async Task Trendyol_push_is_not_sent_when_the_user_declines_the_confirmation()
    {
        ConfirmReturns(ConfirmDialogResult.No);
        var cut = RenderActions(TrendyolContext());

        await ClickAsync(cut, "TrendyolProduct:Push");

        // Onay servis çağrısından ÖNCE ve her tıklamada sorulur; "Hayır" → pazaryerine hiçbir şey gitmez.
        await _ui.ReceivedWithAnyArgs(1).ConfirmAsync(default!, default, default, default, default, default, default);
        await _trendyol.DidNotReceive().PushToTrendyolAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task Trendyol_push_is_sent_once_after_confirmation_and_notifies_the_owner()
    {
        ConfirmReturns(ConfirmDialogResult.Yes);
        var context = TrendyolContext();
        var changed = 0;
        var cut = RenderActions(context, onChanged: () => changed++);

        await ClickAsync(cut, "TrendyolProduct:Push");

        await _trendyol.Received(1).PushToTrendyolAsync(context.ChannelProductId);
        _ui.Received(1).ShowSuccessToast("TrendyolProduct:PushSuccess");
        changed.ShouldBe(1);
    }

    [Fact]
    public async Task N11_push_goes_without_a_confirmation_dialog()
    {
        var context = N11Context();
        var cut = RenderActions(context);

        await ClickAsync(cut, "N11Product:Push");

        await _ui.DidNotReceiveWithAnyArgs().ConfirmAsync(default!, default, default, default, default, default, default);
        await _n11.Received(1).PushToN11Async(context.ChannelProductId);
    }

    [Fact]
    public async Task Sync_warnings_of_a_successful_action_are_shown_one_toast_each()
    {
        var context = N11Context();
        _n11.PushToN11Async(context.ChannelProductId).Returns(Task.FromResult(new SalesChannelTrN11ProductDto
        {
            Id = context.ChannelProductId,
            SyncWarnings = new List<string> { "UYARI-1", "UYARI-2" },
        }));
        var cut = RenderActions(context);

        await ClickAsync(cut, "N11Product:Push");

        // Sessiz geçilmez: kanal bizim gönderdiğimizden farklı bir şey yazdıysa kullanıcı o anda görür.
        _ui.Received(1).ShowWarningToast("UYARI-1");
        _ui.Received(1).ShowWarningToast("UYARI-2");
        _ui.Received(2).ShowWarningToast(Arg.Any<string>());
    }

    [Fact]
    public void Unsaved_channel_product_renders_no_action_buttons()
    {
        var cut = RenderActions(TrendyolContext() with { ChannelProductId = Guid.Empty });

        cut.FindAll("button").ShouldBeEmpty();
        cut.Markup.ShouldContain("N11Product:SaveToPush");
    }

    [Fact]
    public void Etsy_renders_the_coming_soon_text_instead_of_buttons()
    {
        var context = new ChannelProductActionContext(
            SalesChannelType.Etsy, Guid.NewGuid(), Guid.NewGuid(),
            CanPush: false, CanSyncStockPrice: false, CanRefreshStatus: false, CanResolveQueue: false);

        var cut = RenderActions(context);

        cut.FindAll("button").ShouldBeEmpty();
        cut.Markup.ShouldContain("EtsyProduct:PushComingSoon");
    }

    [Fact]
    public void Only_the_permitted_actions_render()
    {
        var cut = RenderActions(TrendyolContext() with { CanSyncStockPrice = false, CanRefreshStatus = true });

        FindButton(cut, "TrendyolProduct:Push").ShouldNotBeNull();
        FindButton(cut, "TrendyolProduct:RefreshStatus").ShouldNotBeNull();
        FindButton(cut, "TrendyolProduct:SyncStockPrice").ShouldBeNull();
        FindButton(cut, "N11Product:ResolvePendingPush").ShouldBeNull();
    }

    // ── Yardımcılar ─────────────────────────────────────────────────────────────────────────────────

    private static ChannelProductActionContext TrendyolContext()
    {
        return new ChannelProductActionContext(
            SalesChannelType.TrTrendyol, Guid.NewGuid(), Guid.NewGuid(),
            CanPush: true, CanSyncStockPrice: true, CanRefreshStatus: false, CanResolveQueue: false);
    }

    private static ChannelProductActionContext N11Context()
    {
        return new ChannelProductActionContext(
            SalesChannelType.TrN11, Guid.NewGuid(), Guid.NewGuid(),
            CanPush: true, CanSyncStockPrice: false, CanRefreshStatus: false, CanResolveQueue: false);
    }

    private void ConfirmReturns(ConfirmDialogResult result)
    {
        _ui.ConfirmAsync(
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<bool>())
            .Returns(Task.FromResult(result));
    }

    private IRenderedComponent<ChannelProductActions> RenderActions(ChannelProductActionContext context, Action? onChanged = null)
    {
        return Render<ChannelProductActions>(parameters =>
        {
            parameters.Add(p => p.Subject, context);
            if (onChanged is not null)
            {
                parameters.Add(p => p.OnChanged, EventCallback.Factory.Create(this, onChanged));
            }
        });
    }

    /// <summary>Düğmeyi METNİNDEN bulur (lokalizasyon sahtesi anahtarı aynen döndürür → metin = anahtar).</summary>
    private static AngleSharp.Dom.IElement? FindButton(IRenderedComponent<ChannelProductActions> cut, string key)
    {
        return cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains(key));
    }

    private static async Task ClickAsync(IRenderedComponent<ChannelProductActions> cut, string key)
    {
        var button = FindButton(cut, key);
        button.ShouldNotBeNull($"'{key}' düğmesi bulunamadı. Bulunanlar: {string.Join(" | ", cut.FindAll("button").Select(b => b.TextContent.Trim()))}");
        await button!.ClickAsync(new MouseEventArgs());
    }
}
