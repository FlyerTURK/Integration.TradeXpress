using System;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Channels;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Channels;

/// <summary>Kanal KURULUM paneli code-behind (kanal-nötr) — orkestratörü (<see cref="IChannelProvisioningAppService"/>)
/// çağırır, adım-adım sonucu tutar. Kanal bu oturumda YENİ oluşturulduysa (<see cref="AutoRun"/>) panel ilk görünümünde
/// kurulumu kendisi başlatır; "Yeniden Kur" idempotent tekrarlar (aynı kilit paylaşılır). Adım hataları sunucuda
/// YUTULUR (StepResult'a döner) → panel yalnız sonucu gösterir; yalnız beklenmedik çağrı hatası dostane toast olur.</summary>
public partial class ChannelProvisioningPanel : CrudComponentBase
{
    /// <summary>Kurulumu yapılacak (kaydedilmiş) satış kanalının kimliği.</summary>
    [Parameter, EditorRequired] public Guid SalesChannelId { get; set; }

    /// <summary>Kanal bu oturumda YENİ oluşturuldu (create-success) → panel ilk görünümünde kurulumu otomatik başlatır.
    /// Update yoluyla açılan formda daima false (host OnAfterCreate yalnız yeni kayıtta çalışır).</summary>
    [Parameter] public bool AutoRun { get; set; }

    [Inject] private IChannelProvisioningAppService ProvisioningAppService { get; set; } = default!;
    [Inject] private IUiInteractionService UiService { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    // Çift-tıklama/eşzamanlı istek engeli (otomatik başlatma + elle "Yeniden Kur" aynı kilidi paylaşır).
    private bool _busy;
    private ProvisioningResultDto? _result;

    /// <summary>Create-anı otomatik kurulumu: panel yalnız kaydedilmiş kanalda görünür → yeni kanal kaydedilir
    /// kaydedilmez İLK yaşam döngüsünde kurulum başlar (Etsy AutoImport deseninin genellemesi).</summary>
    protected override async Task OnInitializedAsync()
    {
        if (AutoRun)
        {
            await ProvisionAsync();
        }
    }

    // Kurulumu çalıştır: orkestratör adım-adım sonucu döner (adım hataları rapora yansır, throw etmez); yalnız
    // beklenmedik çağrı hatasını dostane toast'la göster.
    private async Task ProvisionAsync()
    {
        _busy = true;
        try
        {
            _result = await ProvisioningAppService.ProvisionAsync(SalesChannelId);
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["ChannelProvisioning:Failed"].Value);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Durum rozeti rengi — Success yeşil / Skipped gri / Failed kırmızı.</summary>
    private static string StatusColor(ProvisioningStatus status)
    {
        return status switch
        {
            ProvisioningStatus.Success => "#16a34a",
            ProvisioningStatus.Failed => "#dc2626",
            _ => "#6b7280",
        };
    }

    /// <summary>Durum rozeti lokalize etiketi.</summary>
    private string StatusLabel(ProvisioningStatus status)
    {
        return status switch
        {
            ProvisioningStatus.Success => L["ChannelProvisioning:Status:Success"].Value,
            ProvisioningStatus.Failed => L["ChannelProvisioning:Status:Failed"].Value,
            _ => L["ChannelProvisioning:Status:Skipped"].Value,
        };
    }
}
