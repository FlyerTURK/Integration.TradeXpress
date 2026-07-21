using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Countries;
using Integration.TradeXpress.Geography;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Components.Shared;

/// <summary>Yeniden kullanılabilir coğrafya cascade seçici — Ülke → İl/Eyalet → İlçe. Ülke seçilince o ülkenin idari
/// alanları <see cref="IGeographyAppService.GetAdministrativeAreasAsync"/> ile çekilir; veri DB'de yoksa İLK seferde
/// LAZY IMPORT tetiklenir (birkaç sn — combolar pasif + hint gösterilir). Ülke idari-alan seviyesi kullanmıyorsa
/// (dönen liste tek sembolik ana alan) il/eyalet combo'su GİZLENİR, tek alan otomatik seçilip doğrudan ilçe combo'su
/// gösterilir. Seçim <see cref="SelectionChanged"/> ile çağırana bildirilir; çağıran adres modelini doldurur.
/// DUMB-layout istisnası (reusable picker servis çağırabilir — LookupComboBox deseni).</summary>
public partial class GeographyCascadePicker : CrudComponentBase
{
    /// <summary>Geri yükleme — mevcut seçili ülkenin alpha-2 kodu (ör. adres CountryCode). Verilirse ülke combo'su
    /// bu ülkeye set edilir ve idari alanları sessizce (callback'siz) yüklenir.</summary>
    [Parameter] public string? SelectedCountryCode { get; set; }

    /// <summary>Geri yükleme — seçili idari alan id'si (çağıranda varsa). Ülke yüklendikten sonra combo'da seçilir.</summary>
    [Parameter] public Guid? SelectedAdministrativeAreaId { get; set; }

    /// <summary>Geri yükleme — seçili yerellik id'si (çağıranda varsa).</summary>
    [Parameter] public Guid? SelectedLocalityId { get; set; }

    /// <summary>Kullanıcı bir seviye seçtiğinde tetiklenir — güncel coğrafya seçimini (ülke/il/ilçe + kodlar) taşır.
    /// GERİ YÜKLEME tetiklemez (mevcut adres alanları silinmesin diye restore sessizdir).</summary>
    [Parameter] public EventCallback<GeographySelection> SelectionChanged { get; set; }

    [Inject] private ICountryAppService CountryAppService { get; set; } = default!;
    [Inject] private IGeographyAppService GeographyAppService { get; set; } = default!;
    [Inject] private IUiInteractionService UiService { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    // Ülke lookup (host kataloğu + tenant'ın kendileri) — ada göre sıralı gösterilir.
    private List<CountryListDto> _countries = new();
    private Guid? _selectedCountryId;

    // Seçili ülkenin idari alanları (il/eyalet) + seçim. Sembolik ana alanlı ülkede combo gizli (_usesAreaLevel=false).
    private List<AdministrativeAreaDto> _areas = new();
    private Guid? _selectedAreaId;
    private bool _usesAreaLevel = true;

    // Seçili idari alanın yerellikleri (ilçe/şehir) + seçim.
    private List<LocalityDto> _localities = new();
    private Guid? _selectedLocalityId;

    // Seçili yerelliğin mahalleleri — CANLI N11 çekimi (SAKLANMAZ; her seçimde taze). Id = N11 mahalle id'si (string).
    // Yalnız ülke UsesSubLocality ise gösterilir (TR gibi).
    private List<NeighborhoodDto> _neighborhoods = new();
    private string? _selectedNeighborhoodId;
    private bool _usesSubLocalityLevel;

    // Busy bayrakları — veri çekilirken combolar pasif + hint görünür.
    // _importing = il/eyalet dataset importu (ilk sefer) · _loadingNeighborhoods = mahalle canlı N11 çekimi.
    private bool _importing;
    private bool _loadingNeighborhoods;

    // Herhangi bir yükleme sürüyor mu (combolar bu süre boyunca pasif).
    private bool IsBusy => _importing || _loadingNeighborhoods;

    // ── Dinamik combo başlıkları (adres-format metadatası) ────────────────────────────────────────────
    // Seçili ülke (caption metadatası için) — id ile yüklü listeden çözülür.
    private CountryListDto? SelectedCountry =>
        _selectedCountryId is { } id ? _countries.FirstOrDefault(c => c.Id == id) : null;

    // İl/eyalet başlığı seçili ülkenin idari-alan tipine göre (TR→İl, US→Eyalet); ülke yokken generic birleşik etiket.
    private string AreaCaption =>
        SelectedCountry is { } c ? L[$"AddressFieldType:AdministrativeArea:{c.AdministrativeAreaType}"] : L["Geography:AdministrativeArea"];

    // İlçe/şehir başlığı yerellik tipine göre (TR→İlçe, US→Şehir); ülke yokken generic.
    private string LocalityCaption =>
        SelectedCountry is { } c ? L[$"AddressFieldType:Locality:{c.LocalityType}"] : L["Geography:Locality"];

    // Mahalle başlığı alt-yerellik tipine göre (TR→Mahalle); ülke yokken generic.
    private string NeighborhoodCaption =>
        SelectedCountry is { } c ? L[$"AddressFieldType:SubLocality:{c.SubLocalityType}"] : L["Geography:Neighborhood"];

    protected override async Task OnInitializedAsync()
    {
        await LoadCountriesAsync();
        await RestoreSelectionAsync();
    }

    // ── Yükleme ────────────────────────────────────────────────────────────────────────────────────
    private async Task LoadCountriesAsync()
    {
        try
        {
            var result = await CountryAppService.GetListAsync(new CountryListRequestDto { MaxResultCount = 1000 });
            _countries = result.Items.OrderBy(c => c.Name).ToList();
        }
        catch (Exception ex)
        {
            _countries = new List<CountryListDto>();
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
    }

    // Geri yükleme SESSİZ: combo seçimlerini kurar ama callback'i tetiklemez → mevcut adres alanları korunur.
    private async Task RestoreSelectionAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedCountryCode))
        {
            return;
        }

        var country = _countries.FirstOrDefault(
            c => string.Equals(c.Code, SelectedCountryCode, StringComparison.OrdinalIgnoreCase));
        if (country is null)
        {
            return;
        }

        _selectedCountryId = country.Id;
        _usesSubLocalityLevel = country.UsesSubLocality;
        await LoadAreasAsync(country);

        // İdari alan geri yükleme (çağıran id verdiyse ve alan seviyesi kullanılıyorsa).
        if (_usesAreaLevel && SelectedAdministrativeAreaId is { } areaId
            && _areas.Any(a => a.Id == areaId))
        {
            _selectedAreaId = areaId;
            await LoadLocalitiesAsync(areaId);
        }

        // Yerellik geri yükleme.
        if (SelectedLocalityId is { } localityId && _localities.Any(l => l.Id == localityId))
        {
            _selectedLocalityId = localityId;

            // Mahalle combo'sunu CANLI doldur (yalnız ülke mahalle seviyesi kullanıyorsa) — mahalle SAKLANMADIĞINDAN
            // geri-seçilecek id yok; combo yalnız kullanıcı yeniden seçebilsin diye doldurulur (adres serbest-metni korunur).
            if (_usesSubLocalityLevel)
            {
                await LoadNeighborhoodsAsync(localityId);
            }
        }
    }

    // ── Kullanıcı seçimleri (cascade + callback) ─────────────────────────────────────────────────────
    // Ülke değişti → il/ilçe durumunu sıfırla, alanları yükle (sembolikse otomatik seç), seçimi bildir.
    private async Task OnCountrySelectedAsync(Guid? countryId)
    {
        _selectedCountryId = countryId;
        ResetAreaState();
        ResetLocalityState();
        ResetNeighborhoodState();
        _usesSubLocalityLevel = false;

        if (countryId is not { } id)
        {
            // Picker temizlendi — adres alanlarını EZME (yardımcı iptal edildi say). Callback yok.
            return;
        }

        var country = _countries.FirstOrDefault(c => c.Id == id);
        if (country is null)
        {
            return;
        }

        _usesSubLocalityLevel = country.UsesSubLocality;
        await LoadAreasAsync(country);
        await EmitSelectionAsync();
    }

    // İl/eyalet değişti → ilçe + mahalleyi sıfırla, yeni alanın ilçelerini yükle, seçimi bildir.
    private async Task OnAreaSelectedAsync(Guid? areaId)
    {
        _selectedAreaId = areaId;
        ResetLocalityState();
        ResetNeighborhoodState();

        if (areaId is { } id)
        {
            await LoadLocalitiesAsync(id);
        }

        await EmitSelectionAsync();
    }

    // İlçe değişti → mahalleyi sıfırla; ülke mahalle seviyesi kullanıyorsa mahalleleri CANLI N11'den çek (saklamaz).
    private async Task OnLocalitySelectedAsync(Guid? localityId)
    {
        _selectedLocalityId = localityId;
        ResetNeighborhoodState();

        if (localityId is { } id && _usesSubLocalityLevel)
        {
            await LoadNeighborhoodsAsync(id);
        }

        await EmitSelectionAsync();
    }

    // Mahalle değişti → seçimi bildir.
    private async Task OnNeighborhoodSelectedAsync(string? neighborhoodId)
    {
        _selectedNeighborhoodId = neighborhoodId;
        await EmitSelectionAsync();
    }

    // ── Alan/yerellik yükleyiciler ───────────────────────────────────────────────────────────────────
    // Ülkenin idari alanlarını çeker (DB'de yoksa lazy import — birkaç sn). Dönen liste tek SEMBOLİK ana alansa
    // (ISO kodu yok) ya da ülke bayrağı idari-alan kullanmıyorsa: combo gizlenir, alan otomatik seçilir, ilçeler yüklenir.
    private async Task LoadAreasAsync(CountryListDto country)
    {
        _importing = true;
        await InvokeAsync(StateHasChanged); // spinner/pasif combo hemen görünsün (lazy import öncesi)
        try
        {
            var result = await GeographyAppService.GetAdministrativeAreasAsync(country.Id);
            _areas = result.Items.OrderBy(a => a.Name).ToList();

            // Sembolik ana alan tespiti: tek alan + ISO kodu yok (import sonrası otoriter sinyal). Ülke bayrağı da
            // (UsesAdministrativeArea=false) sembolik işaret eder — ilk importta bayrak henüz true olabileceğinden
            // veri-tabanlı tespit esas alınır, bayrak ikincil ipucu.
            var symbolic = (_areas.Count == 1 && _areas[0].Iso3166_2Code is null)
                           || (!country.UsesAdministrativeArea && _areas.Count > 0);
            if (symbolic)
            {
                _usesAreaLevel = false;
                _selectedAreaId = _areas[0].Id;
                await LoadLocalitiesAsync(_areas[0].Id);
            }
            else
            {
                _usesAreaLevel = true;
            }
        }
        catch (Exception ex)
        {
            _areas = new List<AdministrativeAreaDto>();
            _usesAreaLevel = true;
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
        finally
        {
            _importing = false;
        }
    }

    private async Task LoadLocalitiesAsync(Guid areaId)
    {
        try
        {
            var result = await GeographyAppService.GetLocalitiesAsync(areaId);
            _localities = result.Items.OrderBy(l => l.Name).ToList();
        }
        catch (Exception ex)
        {
            _localities = new List<LocalityDto>();
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
    }

    // Yerelliğin mahallelerini CANLI N11'den çeker (SAKLANMAZ — her seçimde taze; birkaç sn, ayrı busy hint'i).
    // Yalnız ülke mahalle seviyesi kullanıyorsa çağrılır.
    private async Task LoadNeighborhoodsAsync(Guid localityId)
    {
        _loadingNeighborhoods = true;
        await InvokeAsync(StateHasChanged); // spinner/pasif combo hemen görünsün (canlı çekim öncesi)
        try
        {
            var result = await GeographyAppService.GetNeighborhoodsAsync(localityId);
            _neighborhoods = result.OrderBy(n => n.Name).ToList();
        }
        catch (Exception ex)
        {
            _neighborhoods = new List<NeighborhoodDto>();
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
        finally
        {
            _loadingNeighborhoods = false;
        }
    }

    // ── Callback ─────────────────────────────────────────────────────────────────────────────────────
    // Güncel seçimden GeographySelection kurar ve çağırana bildirir. Sembolik ülkede idari alan ADI/KODU
    // BİLDİRİLMEZ (City doldurulmaz — kullanıcı kararı: sembolik alan UI'da gizli).
    private async Task EmitSelectionAsync()
    {
        if (_selectedCountryId is not { } countryId)
        {
            return;
        }

        var country = _countries.FirstOrDefault(c => c.Id == countryId);
        if (country is null)
        {
            return;
        }

        var area = _selectedAreaId is { } aid ? _areas.FirstOrDefault(a => a.Id == aid) : null;
        var locality = _selectedLocalityId is { } lid ? _localities.FirstOrDefault(l => l.Id == lid) : null;
        var neighborhood = _selectedNeighborhoodId is { } nid ? _neighborhoods.FirstOrDefault(n => n.Id == nid) : null;

        var areaName = _usesAreaLevel ? area?.Name : null;
        var areaCode = _usesAreaLevel ? area?.Code : null;
        var areaIsoCode = _usesAreaLevel ? area?.Iso3166_2Code : null;

        // Mahalle seviyesi kullanılmayan ülkede mahalle ad/id'si BİLDİRİLMEZ (combo gizli — Neighborhood doldurulmaz).
        // Mahalle SAKLANMADIĞINDAN yalnız ad (ve canlı N11 id'si) taşınır; adres yalnız adı serbest-metin tutar.
        var neighborhoodId = _usesSubLocalityLevel ? neighborhood?.Id : null;
        var neighborhoodName = _usesSubLocalityLevel ? neighborhood?.Name : null;

        await SelectionChanged.InvokeAsync(new GeographySelection(
            CountryId: country.Id,
            CountryCode: country.Code,
            AdministrativeAreaId: area?.Id,
            AdministrativeAreaName: areaName,
            AdministrativeAreaCode: areaCode,
            AdministrativeAreaIsoCode: areaIsoCode,
            LocalityId: locality?.Id,
            LocalityName: locality?.Name,
            LocalityCode: locality?.Code,
            NeighborhoodId: neighborhoodId,
            NeighborhoodName: neighborhoodName));
    }

    private void ResetAreaState()
    {
        _areas = new List<AdministrativeAreaDto>();
        _selectedAreaId = null;
        _usesAreaLevel = true;
    }

    private void ResetLocalityState()
    {
        _localities = new List<LocalityDto>();
        _selectedLocalityId = null;
    }

    private void ResetNeighborhoodState()
    {
        _neighborhoods = new List<NeighborhoodDto>();
        _selectedNeighborhoodId = null;
    }
}

/// <summary>Coğrafya cascade seçiminin anlık görüntüsü — ülke/il-eyalet/ilçe id + ad + kod (+ il ISO 3166-2 kodu),
/// mahalle ad + (canlı N11) id. Çağıran (ör. ShipmentAddressFields) adres modelini doldurur: il → City, il kodu →
/// CityCode, il ISO kodu → AdministrativeAreaIsoCode, il id → AdministrativeAreaId, ilçe → District, ilçe kodu →
/// DistrictCode, ilçe id → LocalityId, mahalle ADI → Neighborhood. Sembolik ana alanlı ülkede AdministrativeAreaName/Code null gelir
/// (City doldurulmaz); mahalle seviyesi kullanmayan ülkede NeighborhoodName/Id null gelir (Neighborhood doldurulmaz).
/// Mahalle SAKLANMADIĞINDAN <see cref="NeighborhoodId"/> canlı N11 id'sidir (string) ve adres yalnız adı tutar.</summary>
public record GeographySelection(
    Guid CountryId,
    string CountryCode,
    Guid? AdministrativeAreaId,
    string? AdministrativeAreaName,
    string? AdministrativeAreaCode,
    string? AdministrativeAreaIsoCode,
    Guid? LocalityId,
    string? LocalityName,
    string? LocalityCode,
    string? NeighborhoodId,
    string? NeighborhoodName);
