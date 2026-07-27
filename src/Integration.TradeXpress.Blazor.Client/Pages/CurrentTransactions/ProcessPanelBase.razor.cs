using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.CurrentTransactions;

/// <summary>
/// Fiş satırı panellerinin ortak kabuğu: renkli yön şeridi + içerik + Kaydet/Geri butonları.
/// </summary>
public partial class ProcessPanelBase
{
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public ProcessDirectionType Direction { get; set; }
    [Parameter] public EventCallback OnSave { get; set; }
    [Parameter] public EventCallback OnBack { get; set; }

    /// <summary>Kaydet butonu aktifliği — kaydetme sürerken panel false geçer (çift-gönderim koruması).</summary>
    [Parameter] public bool SaveEnabled { get; set; } = true;

    [Parameter] public string? ProcessTypeName { get; set; }
    [Parameter] public string? PaymentTypeName { get; set; }
    [Parameter] public string? AccountCode { get; set; }
    [Parameter] public string? SubAccountCode { get; set; }

    /// <summary>Satırın belge + not ekleri (seri numarası, kamera kaydı, kargo/sigorta evrakı).
    /// <c>null</c> geçilirse ek grubu hiç çizilmez. Liste in-memory düzenlenir; yazımı panel satır
    /// kaydından SONRA yapar (ek satırın kimliğine bağlanır).</summary>
    [Parameter] public VoucherLineAttachmentSet? Attachments { get; set; }

    /// <summary>Düzenlenen satır — karşı hesap seçimi doğrudan bunun üzerinde tutulur.
    /// <c>null</c> geçilirse karşı hesap bölümü çizilmez.</summary>
    [Parameter] public VoucherLineDto? Line { get; set; }

    /// <summary>Fişin KENDİ alt hesabı — karşı hesap listesinden elenir (kendine virman/ayna olmaz).</summary>
    [Parameter] public Guid? OwnSubAccountId { get; set; }

    /// <summary>Karşı hesap bölümü gösterilsin mi. Virmanda alan ZORUNLU ve kendi panelinde yönetildiği için
    /// orada kapatılır; diğer tiplerde opsiyoneldir.</summary>
    [Parameter] public bool ShowCounterAccount { get; set; } = true;

    [Inject] private ISubAccountAppService SubAccountService { get; set; } = default!;

    /// <summary>SALT-OKUNUR görüntüleme: işlem geçmişindeki bir kaydın o günkü hâlini göstermek için.
    /// <para>İçerik etkileşime kapatılır (<c>pointer-events:none</c>) ve Kaydet gizlenir — on iki panelin
    /// her kontrolüne <c>Enabled=false</c> geçirmek yerine TEK yerden. Gerekçe: yeni alan ekleyen biri
    /// salt-okunur bayrağını unutabilir, bu yolda unutulamaz.</para></summary>
    [Parameter] public bool ReadOnly { get; set; }

    /// <summary>Dar ekran (mobil) mı — panelin KENDİ <c>DxLayoutBreakpoint</c>'inden gelir.
    /// Kabuk ayrı bir breakpoint tutmaz: iki ayrı kırılım noktası aynı anda çalışınca etiket
    /// görünürlükleri çakışıyordu (masaüstünde gizlenmesi gereken başlıklar çiziliyordu).</summary>
    [Parameter] public bool IsNarrow { get; set; }

    private List<SubAccountListDto> _counterAccounts = new();
    private List<YesNoItem> _yesNoItems = new();
    private bool _useCounterAccount;
    private bool _isNarrow => IsNarrow;

    /// <summary>Evet/Hayır combo öğesi.</summary>
    private sealed record YesNoItem(bool Value, string Text);

    protected override async Task OnInitializedAsync()
    {
        _yesNoItems = new List<YesNoItem>
        {
            new(false, L["No"].Value),
            new(true, L["Yes"].Value),
        };

        if (ShowCounterAccount)
        {
            var subResult = await SubAccountService.GetListAsync(new SubAccountListRequestDto { MaxResultCount = 1000 });
            _counterAccounts = subResult.Items
                .Where(s => s.IsActive && s.Id != OwnSubAccountId)
                .ToList();
        }
    }

    /// <summary>Ek grubu açık mı — KAPALI başlar; kullanıcı açabilir, ama yeni satıra geçilince yeniden katlanır.</summary>
    private bool _attachmentsExpanded;

    /// <summary>Ek setinin en son görülen sıfırlama sayacı — değişmesi "yeni satır başladı" demektir.</summary>
    private int _seenAttachmentReset;

    protected override void OnParametersSet()
    {
        // Düzenlemeye açılan satırda karşı hesap doluysa anahtar EVET'te başlar (kullanıcı seçimini görsün).
        if (Line?.CounterAccountId is { } existing && existing != Guid.Empty)
        {
            _useCounterAccount = true;
        }

        // Başarılı kayıttan sonra panel yeni satır için sıfırlanır → ek grubu da yeniden KATLANIR.
        // (Kullanıcı önceki satır için açmışsa, sonraki satırda açık kalmasın.)
        if (Attachments is not null && Attachments.ResetCount != _seenAttachmentReset)
        {
            _seenAttachmentReset = Attachments.ResetCount;
            _attachmentsExpanded = false;
        }
    }

    /// <summary>HAYIR'a dönülünce seçim TEMİZLENİR — aksi hâlde gizli bir seçim ayna fiş açtırırdı.</summary>
    private void OnUseCounterAccountChanged(bool value)
    {
        _useCounterAccount = value;
        if (!value && Line is not null)
        {
            Line.CounterAccountId = null;
        }
    }

    /// <summary>Grup başlığı: "Dokümanlar ve Notlar"; dolu olan tarafın yanında adet — ör.
    /// "Dokümanlar (2) ve Notlar (3)". Boş taraf sayısız kalır ki katlı grup gereksiz dikkat çekmesin.</summary>
    private string AttachmentsCaption()
    {
        var documents = L["Documents"].Value;
        var notes = L["Notes"].Value;

        if (Attachments is not null)
        {
            if (Attachments.Documents.Count > 0)
            {
                documents = $"{documents} ({Attachments.Documents.Count})";
            }

            if (Attachments.Notes.Count > 0)
            {
                notes = $"{notes} ({Attachments.Notes.Count})";
            }
        }

        return $"{documents} {L["And"].Value} {notes}";
    }

    private string StripText()
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(ProcessTypeName)) parts.Add(ProcessTypeName.ToUpperInvariant());
        parts.Add(L[$"Enum:ProcessDirectionType:{Direction}"].Value.ToUpperInvariant());
        if (!string.IsNullOrEmpty(PaymentTypeName)) parts.Add(PaymentTypeName.ToUpperInvariant());

        var text = string.Join("   ", parts);

        if (!string.IsNullOrEmpty(AccountCode))
        {
            var account = string.IsNullOrEmpty(SubAccountCode)
                ? $"[{AccountCode}]"
                : $"[{AccountCode} / {SubAccountCode}]";
            text += $"   {account}";
        }

        return text;
    }

    private string StripStyle()
    {
        // inflow (Giriş/Alacak/Alış) → yeşil; aksi (Çıkış/Borç/Satış) → kırmızı.
        var isInflow = Direction.IsInflow();
        var gradient = isInflow
            ? "var(--gradient-green)"
            : "var(--gradient-red)";

        return $"height:34px; border-radius:4px 4px 0 0; background:{gradient};";
    }
}
