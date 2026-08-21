using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.EtsyProducts;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.SalesChannelProducts;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.TrendyolProducts;
using Integration.TradeXpress.Blazor.Client.Pages.N11Products;
using Integration.TradeXpress.Blazor.Client.Pages.TrendyolProducts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Volo.Abp;
using Volo.Abp.ObjectMapping;

namespace Integration.TradeXpress.Blazor.Client.Pages.SalesChannelProducts;

/// <summary>
/// Kanal-ürünü düzenleme bileşeni — kanal türüne göre tipli alan setini yükler ve tipli servisle kaydeder.
///
/// <para><b>Model alanları AYRI tutulur (tek <c>object</c> değil):</b> Razor'da tipli bileşene bağlanacak
/// modelin derleme zamanında bilinmesi gerekir; tek ortak alan tutup her kullanımda cast etmek, yanlış
/// kanalın formuna yanlış modeli bağlama hatasını ÇALIŞMA ZAMANINA ertelerdi.</para>
///
/// <para><b>Kaydetmeyi drill tetikler:</b> <see cref="SaveAsync"/> panelin <c>PersistUpdate</c> callback'inden
/// çağrılır. Doğrulama BURADA yapılır (tipli modele bağlı kendi EditContext'i ile); geçmezse istisna
/// fırlatılır ve drill popup'ı AÇIK bırakıp mesajı gösterir — kullanıcı girdisini kaybetmez.</para>
///
/// <para><b>Ürün grafına dokunulmaz:</b> kanal-ürünün ERP varyant listesi (<c>CoreVariants</c>) burada
/// VERİLMEZ — o bilgi ürün formunun bağlamıdır. Alan setleri onu opsiyonel taşır ve yokluğunda kanal-özel
/// alanlar aynen düzenlenebilir.</para>
/// </summary>
public partial class ChannelProductEditFields : IDisposable
{
    /// <summary>Düzenlenecek satır (hafif liste DTO'su) — tam graf bundan türetilerek çekilir.</summary>
    [Parameter, EditorRequired] public SalesChannelProductListDto Row { get; set; } = default!;

    [Inject] private ISalesChannelTrN11ProductAppService N11AppService { get; set; } = default!;
    [Inject] private ISalesChannelTrTrendyolProductAppService TrendyolAppService { get; set; } = default!;
    [Inject] private ISalesChannelEtsyProductAppService EtsyAppService { get; set; } = default!;
    [Inject] private IObjectMapper Mapper { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    private bool _loading;
    private string? _loadError;

    /// <summary>Yüklenmiş satırın kimliği — parametre yeniden atandığında gereksiz yeniden çekimi önler.</summary>
    private Guid _loadedId;

    private EditContext? _editContext;

    // Üç kanalın modeli — yalnız BİRİ dolu (gerekçe: tip özeti).
    private SalesChannelTrN11ProductDto? _n11Model;
    private SalesChannelTrTrendyolProductDto? _trendyolModel;
    private SalesChannelEtsyProductDto? _etsyModel;
    private SalesChannelTrTrendyolProductEditFields? _trendyolFields;
    private SalesChannelTrN11ProductEditFields? _n11Fields;

    /// <summary>Alan setlerinin beklediği kanal listesi — tek öğeli (kanal SET-ONCE'tur, burada değişmez).</summary>
    private List<SalesChannelListDto> _channelAsList = new();

    protected override async Task OnParametersSetAsync()
    {
        if (Row.Id == _loadedId)
        {
            return;
        }

        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        _loadError = null;
        _n11Model = null;
        _trendyolModel = null;
        _etsyModel = null;
        _editContext = null;

        _channelAsList = new List<SalesChannelListDto>
        {
            new()
            {
                Id = Row.SalesChannelId,
                Code = Row.SalesChannelCode ?? string.Empty,
                Name = Row.SalesChannelName ?? string.Empty,
                ChannelType = Row.ChannelType,
                IsActive = true,
            },
        };

        try
        {
            switch (Row.ChannelType)
            {
                case SalesChannelType.TrN11:
                    _n11Model = await N11AppService.GetAsync(Row.Id);
                    _editContext = new EditContext(_n11Model);
                    break;

                case SalesChannelType.TrTrendyol:
                    _trendyolModel = await TrendyolAppService.GetAsync(Row.Id);
                    _editContext = new EditContext(_trendyolModel);
                    break;

                case SalesChannelType.Etsy:
                    _etsyModel = await EtsyAppService.GetAsync(Row.Id);
                    _editContext = new EditContext(_etsyModel);
                    break;

                default:
                    // Bilinmeyen tür SESSİZ GEÇİLMEZ — bileşen sebebi yazar, boş form gösterilmez.
                    _loadError = L["SalesChannelProduct:UnsupportedChannel"].Value;
                    break;
            }

            _loadedId = Row.Id;
        }
        catch (Exception ex)
        {
            _loadError = CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? ex.Message;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Alt form kirli mi — panelin drill'i Kaydet aktifliğini bununla sürer (<c>EditDirtyProvider</c>).
    /// Drill'in kendi JSON kıyası liste satırına (Row) baktığından bu formun düzenlemesini HİÇ göremez; dirty
    /// yalnız buradan bilinebilir (2026-08-15 Hakan: "attribute değişikliği Kaydet'i açmadı").</summary>
    public bool IsDirty => _editContext?.IsModified() == true;

    /// <summary>Alt formda bir alan değişti — panel drill'i yeniden çizip Kaydet'i tazelesin diye yukarı bildirilir.</summary>
    [Parameter] public EventCallback OnDirtyChanged { get; set; }

    private EditContext? _subscribedContext;

    protected override void OnAfterRender(bool firstRender)
    {
        base.OnAfterRender(firstRender);
        if (ReferenceEquals(_subscribedContext, _editContext))
        {
            return;
        }

        if (_subscribedContext is not null)
        {
            _subscribedContext.OnFieldChanged -= OnAnyFieldChanged;
        }

        _subscribedContext = _editContext;
        if (_subscribedContext is not null)
        {
            _subscribedContext.OnFieldChanged += OnAnyFieldChanged;
        }
    }

    private void OnAnyFieldChanged(object? sender, FieldChangedEventArgs e)
    {
        _ = InvokeAsync(() => OnDirtyChanged.InvokeAsync());
    }

    public void Dispose()
    {
        if (_subscribedContext is not null)
        {
            _subscribedContext.OnFieldChanged -= OnAnyFieldChanged;
        }
    }

    /// <summary>Tipli kaydetme — panelin <c>PersistUpdate</c>'inden çağrılır. Doğrulama geçmezse ya da model
    /// yüklenememişse İSTİSNA fırlatır: drill bunu yakalayıp popup'ı açık bırakır ve mesajı gösterir
    /// (sessizce "kaydedildi" demek veri kaybıdır).</summary>
    public async Task SaveAsync()
    {
        if (_editContext is null)
        {
            throw new BusinessException(
                "SalesChannelProduct:UnsupportedChannel",
                _loadError ?? L["SalesChannelProduct:UnsupportedChannel"].Value);
        }

        // Açık hücre düzenlemesi (kategori nitelik grid'i) kaydetmeden ÖNCE kapatılır — aksi hâlde seçilen değer
        // edit-model'de kalır ve sessizce kaybolur.
        if (_trendyolFields is not null)
        {
            await _trendyolFields.CommitPendingAttributeEditAsync();
        }

        if (_n11Fields is not null)
        {
            await _n11Fields.CommitPendingAttributeEditAsync();
        }

        if (!_editContext.Validate())
        {
            throw new BusinessException(
                "SalesChannelProduct:FixValidationErrors",
                L["SalesChannelProduct:FixValidationErrors"].Value);
        }

        switch (Row.ChannelType)
        {
            case SalesChannelType.TrN11 when _n11Model is not null:
                var n11Input = Mapper.Map<SalesChannelTrN11ProductDto, SalesChannelTrN11ProductUpdateDto>(_n11Model);
                _n11Model = await N11AppService.UpdateAsync(Row.Id, n11Input);
                break;

            case SalesChannelType.TrTrendyol when _trendyolModel is not null:
                var trendyolInput = Mapper.Map<SalesChannelTrTrendyolProductDto, SalesChannelTrTrendyolProductUpdateDto>(_trendyolModel);
                _trendyolModel = await TrendyolAppService.UpdateAsync(Row.Id, trendyolInput);
                break;

            case SalesChannelType.Etsy when _etsyModel is not null:
                var etsyInput = Mapper.Map<SalesChannelEtsyProductDto, SalesChannelEtsyProductUpdateDto>(_etsyModel);
                _etsyModel = await EtsyAppService.UpdateAsync(Row.Id, etsyInput);
                break;

            default:
                throw new BusinessException(
                    "SalesChannelProduct:UnsupportedChannel",
                    L["SalesChannelProduct:UnsupportedChannel"].Value);
        }
    }
}
