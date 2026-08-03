using System;
using System.Threading.Tasks;
using DevExpress.Blazor;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.ChannelQuestions;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Volo.Abp;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Blazor.Client.Pages.ChannelQuestions;

/// <summary>
/// Ürün soruları gelen kutusu (MDI sekme) — TÜM satış kanallarının müşteri soruları TEK grid'de
/// (kanal yalnız bir kolon). Server-side sayfalama <see cref="GridListDataSource{TListDto}"/> ile
/// <see cref="IChannelQuestionAppService.GetListAsync"/>'e bağlıdır.
///
/// <para><b>Neden CrudLayout değil:</b> bu bir KATALOG değil gelen kutusu — Yeni/Sil yok (kayıtlar
/// yalnız kanaldan çekilir) ve satır tıklaması bir edit formu değil CEVAP popup'ı açar. CrudLayout'un
/// getirdiği yeni/sil/aktiflik iskeleti burada yanıltıcı affordance olurdu.</para>
///
/// <para><b>Gönderim KAPALI:</b> cevap yalnız YERELDE saklanır (taslak ya da gönderim kuyruğu).
/// Bu yüzden buton "Gönder" değil "Gönderilmeye Hazır"dır ve "Gönderildi" ibaresi yalnız
/// <see cref="ChannelAnswerState.Sent"/> rozetinde geçer — bugün hiçbir satır o duruma geçmez.</para>
///
/// <para><b>Bu sayfa pazaryerine ASLA doğrudan GİTMEZ.</b> N11 ürün sorularını hesap başına dakikada bir kez
/// listelemeye izin verir ve bu kotayı eşzamanlılık aşmaz → çekimi yapan TEK merkez arka plan işçisidir.
/// Sayfa açılışı ve "Yenile" yalnız <see cref="IChannelQuestionAppService.RequestSyncAsync"/> ile kuyruğa
/// işaret bırakır; grid o sırada ELDEKİ veriyle çizilir. Bu yüzden kullanıcıya "çekiliyor" DENMEZ,
/// "sıraya alındı" denir — aksi hâlde listenin hemen tazeleneceği vaat edilmiş olurdu.</para>
/// </summary>
public partial class ChannelQuestionListPage : IDisposable
{
    #region Constants

    /// <summary>Grid hücresinde gösterilen soru önizlemesinin azami uzunluğu (tamamı popup'ta).</summary>
    private const int QuestionPreviewLength = 90;

    /// <summary>Bekleyen sayacı için sunucudan istenen satır sayısı — ilgilendiğimiz tek şey
    /// <c>TotalCount</c> olduğundan tam sayfa çekmek gereksiz trafik olurdu.</summary>
    private const int CountProbeSize = 1;

    #endregion

    #region Injected

    [Inject] private IChannelQuestionAppService QuestionAppService { get; set; } = default!;

    [Inject] private IUiInteractionService Ui { get; set; } = default!;

    #endregion

    #region State

    private TxGrid? _grid;

    private GridListDataSource<ChannelQuestionListDto>? _dataSource;

    /// <summary>Varsayılan AÇIK: gelen kutusunun asıl işi cevap bekleyenlerdir; tüm arşiv switch ile açılır.</summary>
    private bool _onlyPending = true;

    /// <summary>Cevap bekleyen soru adedi (filtreden BAĞIMSIZ — kuyruğun büyüdüğü görünsün).</summary>
    private long _pendingCount;

    /// <summary>Son fetch 0 satır döndürdü mü (boş liste ipucu için).</summary>
    private bool _isEmpty;

    private bool _canAnswer;

    /// <summary>Çekimi SIRAYA alma yetkisi. Yoksa "Yenile" yalnız listeyi tazeler — kuyruğa istek GÖNDERİLMEZ
    /// (sunucu zaten reddederdi; kullanıcıya olmayan bir güncelleme vaat edilmesin).</summary>
    private bool _canSync;

    // ── Cevap popup'ı ──
    private bool _popupVisible;
    private ChannelQuestionListDto? _row;
    private string? _answerText;
    private string? _error;
    private bool _busy;

    /// <summary>Server-side grid kaynağı — sayfalama/sıralama sunucuya nötr <see cref="ListRequestDto"/> olarak gider.</summary>
    public GridListDataSource<ChannelQuestionListDto> DataSource
    {
        get
        {
            if (_dataSource is null)
            {
                _dataSource = new GridListDataSource<ChannelQuestionListDto>(FetchPageAsync)
                {
                    OnError = ShowFetchErrorAsync,
                };
                _dataSource.Settled += OnDataSettled;
            }

            return _dataSource;
        }
    }

    /// <summary>Gönderilmiş cevap yerelde de değiştirilemez (pazaryerinde düzenleme/silme operasyonu yok).</summary>
    private bool IsAnswerLocked
    {
        get { return _row is { AnswerState: ChannelAnswerState.Sent }; }
    }

    /// <summary>Cevap kutusu ve kaydet butonları etkin mi — izin + kilit birlikte.</summary>
    private bool CanWriteAnswer
    {
        get { return _canAnswer && !IsAnswerLocked; }
    }

    #endregion

    #region Lifecycle

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();

        _canAnswer = await AuthorizationService.IsGrantedAsync(TradeXpressPermissions.ChannelQuestions.Answer);
        _canSync   = await AuthorizationService.IsGrantedAsync(TradeXpressPermissions.ChannelQuestions.Sync);

        await RefreshPendingCountAsync();

        // Sayfa açılışı çekimi SESSİZ sıraya alır: grid zaten eldeki veriyle çizilir, bu çağrı yalnız bir
        // sonraki işçi turuna öncelik işareti bırakır ve anında döner. Toast YOK — kullanıcının yapmadığı bir
        // eylem için bildirim gürültüdür; "sıraya alındı" bilgisi ancak AÇIK bir Yenile'nin cevabı olarak anlamlı.
        await QueueSyncAsync(notifyUser: false);
    }

    public void Dispose()
    {
        if (_dataSource is not null)
        {
            _dataSource.Settled -= OnDataSettled;
        }
    }

    #endregion

    #region Data

    private Task<PagedResultDto<ChannelQuestionListDto>> FetchPageAsync(ListRequestDto request)
    {
        var typed = new ChannelQuestionListRequestDto
        {
            SkipCount      = request.SkipCount,
            MaxResultCount = request.MaxResultCount,
            Sorting        = request.Sorting,
            Filter         = request.Filter,
            Sorts          = request.Sorts,
            Filters        = request.Filters,
            IsActive       = request.IsActive,
            OnlyPending    = _onlyPending,
        };

        return QuestionAppService.GetListAsync(typed);
    }

    /// <summary>Toolbar "Yenile" — ÖNCE eldeki veriyi tazeler (grid + bekleyen sayacı), SONRA çekimi sıraya alır.
    /// <para>Sıra bilinçli: tazeleme kullanıcının hemen göreceği iştir; kuyruk işareti ise en erken bir sonraki
    /// işçi turunda meyve verir. İkisi TEK butonda birleşir çünkü kullanıcı için ayrı iki eylem değildir —
    /// ama geri bildirim ayrı ayrı dürüsttür: liste ŞİMDİ tazelendi, çekim SIRAYA alındı.</para></summary>
    private async Task RefreshAsync()
    {
        ReloadGrid();
        await RefreshPendingCountAsync();
        await QueueSyncAsync(notifyUser: true);
    }

    /// <summary>Kanal çekimini SIRAYA alır — bu çağrı pazaryerine GİTMEZ ve hiçbir soru getirmez.
    /// <paramref name="notifyUser"/> yalnız AÇIK kullanıcı eyleminde (Yenile) true'dur; bildirim metni
    /// "sıraya alındı"dır — "çekiliyor" demek listenin şimdi tazeleneceğini vaat ederdi.</summary>
    private async Task QueueSyncAsync(bool notifyUser)
    {
        if (!_canSync)
        {
            return;
        }

        try
        {
            await QuestionAppService.RequestSyncAsync();

            if (notifyUser)
            {
                Ui.ShowSuccessToast(L["ChannelQuestion:SyncQueued"]);
            }
        }
        catch (Exception ex)
        {
            // Kuyruk işareti YARDIMCI bir iştir: başarısız olsa da liste eldeki veriyle çalışmaya devam etmeli
            // (sayaç hatasıyla aynı gerekçe) — sessizce yutulmaz ama sayfayı da düşürmez.
            Ui.ShowErrorToast(Describe(ex));
        }
    }

    /// <summary>Bekleyen kuyruğunun boyu. Yalnız <c>TotalCount</c> gerektiği için tek satır istenir.</summary>
    private async Task RefreshPendingCountAsync()
    {
        try
        {
            var pending = await QuestionAppService.GetListAsync(new ChannelQuestionListRequestDto
            {
                OnlyPending    = true,
                SkipCount      = 0,
                MaxResultCount = CountProbeSize,
            });

            _pendingCount = pending.TotalCount;
        }
        catch (Exception ex)
        {
            // Sayaç yardımcı bilgidir; alınamazsa sayfa çalışmaya devam etmeli (grid kendi hatasını gösterir).
            Ui.ShowErrorToast(Describe(ex));
        }
    }

    /// <summary>Filtre switch'i — SENKRON: sunucuya gidecek iş grid'in kendi fetch'inde yapılır.</summary>
    private void OnOnlyPendingChanged(bool value)
    {
        _onlyPending = value;
        ReloadGrid();
    }

    private void ReloadGrid()
    {
        _grid?.InnerGrid?.Reload();
    }

    /// <summary>Fetch turu bitti → boş-liste ipucunu senkronla (yalnız DEĞİŞİNCE render tetikle).</summary>
    private void OnDataSettled()
    {
        var empty = DataSource.TotalCount == 0;
        if (empty == _isEmpty)
        {
            return;
        }

        _isEmpty = empty;
        InvokeAsync(StateHasChanged);
    }

    /// <summary>Satır tıklaması cevap popup'ını açtığı için satırlar üzerinde el (pointer) imleci —
    /// tıklanabilirlik keşfedilebilir olsun (CrudLayout ile aynı davranış).</summary>
    private static void OnCustomizeGridElement(GridCustomizeElementEventArgs e)
    {
        if (e.ElementType == GridElementType.DataRow)
        {
            e.Style = "cursor: pointer;";
        }
    }

    private Task ShowFetchErrorAsync(Exception ex)
    {
        return InvokeAsync(() =>
        {
            Ui.ShowErrorToast(Describe(ex));
            StateHasChanged();
        });
    }

    #endregion

    #region Answer popup

    /// <summary>Satır tıklaması → cevap popup'ı. SENKRON: sunucuya gitmeden açılır (satır DTO'su zaten tam).</summary>
    private void OnRowClick(GridRowClickEventArgs e)
    {
        if (_grid?.InnerGrid?.GetDataItem(e.VisibleIndex) is not ChannelQuestionListDto row)
        {
            return;
        }

        _row          = row;
        _answerText   = row.AnswerText;
        _error        = null;
        _popupVisible = true;
    }

    private void ClosePopup()
    {
        _popupVisible = false;
    }

    private Task SaveDraftAsync()
    {
        return WriteAnswerAsync(readyToSend: false);
    }

    private Task SaveReadyToSendAsync()
    {
        return WriteAnswerAsync(readyToSend: true);
    }

    /// <summary>Cevabı YERELDE kaydeder. <paramref name="readyToSend"/> yalnız kuyruğa alır — HİÇBİR ŞEY GÖNDERMEZ.</summary>
    private async Task WriteAnswerAsync(bool readyToSend)
    {
        if (_busy || _row is not { } row || !CanWriteAnswer)
        {
            return;
        }

        _busy  = true;
        _error = null;
        try
        {
            var updated = await QuestionAppService.WriteAnswerAsync(row.Id, new ChannelQuestionAnswerInput
            {
                AnswerText   = _answerText,
                ReadyToSend  = readyToSend,
            });

            ApplyUpdated(updated);
            Ui.ShowSuccessToast(L["ChannelQuestion:AnswerSaved"]);
            await RefreshPendingCountAsync();
        }
        catch (Exception ex)
        {
            // Hata popup'ta KALICI kalsın (toast kaçar) — kullanıcı yazdığı cevabı kaybetmeden düzeltebilsin.
            var message = Describe(ex);
            _error = message;
            Ui.ShowErrorToast(message);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Okundu/okunmadı — AÇIK kullanıcı eylemi (popup açılınca sessizce damgalanmaz).</summary>
    private async Task ToggleReadAsync()
    {
        if (_busy || _row is not { } row)
        {
            return;
        }

        _busy  = true;
        _error = null;
        try
        {
            var updated = await QuestionAppService.SetReadAsync(row.Id, !row.IsRead);
            ApplyUpdated(updated);
            Ui.ShowSuccessToast(L["ChannelQuestion:ReadStateSaved"]);
        }
        catch (Exception ex)
        {
            var message = Describe(ex);
            _error = message;
            Ui.ShowErrorToast(message);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Sunucunun döndürdüğü güncel satırı popup'a ve grid'e yansıtır.</summary>
    private void ApplyUpdated(ChannelQuestionListDto updated)
    {
        _row        = updated;
        _answerText = updated.AnswerText;
        ReloadGrid();
    }

    #endregion

    #region Display

    /// <summary>Kanal etiketi: önce enum çevirisi, çeviri yoksa sunucunun çözdüğü kanal adı.</summary>
    private string ChannelLabel(ChannelQuestionListDto row)
    {
        var localized = L[$"Enum:SalesChannelType:{row.ChannelType}"];
        if (!localized.ResourceNotFound)
        {
            return localized.Value;
        }

        return string.IsNullOrWhiteSpace(row.SalesChannelName)
            ? row.ChannelType.ToString()
            : row.SalesChannelName!;
    }

    private string StatusLabel(ChannelQuestionStatus status)
    {
        return L[$"Enum:ChannelQuestionStatus:{status}"].Value;
    }

    private string AnswerStateLabel(ChannelAnswerState state)
    {
        return L[$"Enum:ChannelAnswerState:{state}"].Value;
    }

    /// <summary>Soru durumu rozeti — bekleyen amber, kapanan yeşil/gri (Teyit kutusuyla aynı renk dili).</summary>
    private static string StatusBadgeStyle(ChannelQuestionStatus status)
    {
        var background = status switch
        {
            ChannelQuestionStatus.Pending  => "#f59e0b",   // amber — cevap bekliyor (SLA işliyor)
            ChannelQuestionStatus.Answered => "#16a34a",   // yeşil — kanalda cevaplanmış
            ChannelQuestionStatus.Closed   => "#64748b",   // gri-mavi — kapandı, cevap beklenmiyor
            _ => "#6b7280",                                // gri — eşlenemeyen ham durum
        };

        return BuildBadgeStyle(background);
    }

    /// <summary>Cevap teslim durumu rozeti. Yeşil YALNIZ <see cref="ChannelAnswerState.Sent"/>'te —
    /// "gönderildi" izlenimi başka hiçbir durumda verilmez.</summary>
    private static string AnswerBadgeStyle(ChannelAnswerState state)
    {
        var background = state switch
        {
            ChannelAnswerState.Draft       => "#3b82f6",   // mavi — taslak, kuyruğa girmedi
            ChannelAnswerState.ReadyToSend => "#f59e0b",   // amber — kuyrukta bekliyor (push kapalı)
            ChannelAnswerState.Sent        => "#16a34a",   // yeşil — gerçekten gönderildi
            ChannelAnswerState.Failed      => "#dc2626",   // kırmızı — gönderim başarısız
            _ => "#6b7280",                                // gri — henüz cevap yok
        };

        return BuildBadgeStyle(background);
    }

    private static string BuildBadgeStyle(string background)
    {
        return "display:inline-block; padding:2px 8px; border-radius:10px; font-size:12px; "
             + $"font-weight:600; color:#fff; background:{background};";
    }

    /// <summary>Çok satırlı uzun metni tek satırlık grid önizlemesine indirger.</summary>
    private static string Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var singleLine = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (singleLine.Length <= maxLength)
        {
            return singleLine;
        }

        return string.Concat(singleLine.AsSpan(0, maxLength), "...");
    }

    /// <summary>Sunucu hatasını kullanıcı diline çevirir (lokalize error-code); kod yoksa ham mesaj.</summary>
    private string Describe(Exception ex)
    {
        if (ex is not BusinessException { Code: { } code } || string.IsNullOrWhiteSpace(code))
        {
            return ex.Message;
        }

        return L[code].Value;
    }

    #endregion
}
