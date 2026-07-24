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
    /// <summary>Sabit-ülke modu — doluysa ülke combo'su GİZLENİR, cascade bu ülkeye kilitlenir (İl'den başlar).
    /// Şube adresi gibi parent'ın ülkesine kilitli formlar için (ör. company ülkesi). <see cref="SelectedCountryCode"/>
    /// ile çelişirse FIXED ESAS alınır. null → serbest mod (ülke combo'su görünür, mevcut davranış).</summary>
    [Parameter] public Guid? FixedCountryId { get; set; }

    /// <summary>Geri yükleme — mevcut seçili ülkenin alpha-2 kodu (ör. adres CountryCode). Verilirse ülke combo'su
    /// bu ülkeye set edilir ve idari alanları sessizce (callback'siz) yüklenir. <see cref="FixedCountryId"/> doluysa
    /// yok sayılır (fixed öncelikli).</summary>
    [Parameter] public string? SelectedCountryCode { get; set; }

    /// <summary>Geri yükleme — seçili idari alan id'si (çağıranda varsa). Ülke yüklendikten sonra combo'da seçilir.</summary>
    [Parameter] public Guid? SelectedAdministrativeAreaId { get; set; }

    /// <summary>Geri yükleme — seçili yerellik id'si (çağıranda varsa).</summary>
    [Parameter] public Guid? SelectedLocalityId { get; set; }

    /// <summary>Geri yükleme — seçili mahalle ADI (mahalle SAKLANMADIĞINDAN id yok; canlı N11 listesi çekilip ada
    /// göre eşleştirilir). Eşleşme bulunamazsa combo boş kalır ama isim adres modelinde korunur. Yalnız ülke mahalle
    /// seviyesi kullanıyorsa (TR) anlamlıdır.</summary>
    [Parameter] public string? SelectedNeighborhoodName { get; set; }

    /// <summary>Varsayılan ülke — <see cref="FixedCountryId"/> ve <see cref="SelectedCountryCode"/> BOŞ iken (yeni/boş
    /// adres) bu ülke ön-seçilir ve seçim ÇAĞIRANA BİLDİRİLİR (loud) + alt seviyeler ilk değerine iner → adres modeli
    /// company ülkesiyle dolar. Kilit DEĞİL (kullanıcı Ülke combosundan değiştirebilir). Mevcut adres kodu varsa yok
    /// sayılır (kod öncelikli). null → varsayılan yok.</summary>
    [Parameter] public Guid? DefaultCountryId { get; set; }

    /// <summary>Gömülü mod — true iken picker kendi DxFormLayout/DxFormLayoutGroup WRAPPER'ını RENDER ETMEZ; yalnız
    /// combo DxFormLayoutItem'larını render eder → parent'ın grubuna inerler (ör. AddressFields tek grupta birleştirir).
    /// false → standalone (kendi grubunu taşır; mevcut davranış, ör. N11AddressFields).</summary>
    [Parameter] public bool Embedded { get; set; }

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
    // _importing = il/eyalet dataset importu (ülke seçilince ilk sefer) · _loadingLocalities = seçilen eyaletin
    // şehirleri per-state dataset'ten iniyor (o eyalet ilk seçildiğinde) · _loadingNeighborhoods = mahalle canlı N11.
    private bool _importing;
    private bool _loadingLocalities;
    private bool _loadingNeighborhoods;

    // Herhangi bir yükleme sürüyor mu (combolar bu süre boyunca pasif).
    private bool IsBusy => _importing || _loadingLocalities || _loadingNeighborhoods;

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
    // Sabit-ülke modunda (FixedCountryId) o ülke id ile, aksi halde SelectedCountryCode ile çözülür (fixed öncelikli).
    private async Task RestoreSelectionAsync()
    {
        var (country, isDefault) = ResolveInitialCountry();
        if (country is null)
        {
            return;
        }

        _selectedCountryId = country.Id;
        _usesSubLocalityLevel = country.UsesSubLocality;
        await LoadAreasAsync(country);

        // VARSAYILAN ülke yolu (adres boştu, DefaultCountryId ile ön-seçim) → LOUD: alt seviyeleri ilk değerine indir
        // + seçimi çağırana BİLDİR (adres modeli company ülkesi + ilk il/ilçe/mahalle ile dolar). Kullanıcı sonra
        // istediği ülkeyi seçebilir (kilit yok). Sessiz restore'un (mevcut adres) aksine callback tetiklenir.
        if (isDefault)
        {
            await AutoSelectFirstFromAreasAsync();
            await EmitSelectionAsync();
            return;
        }

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
            // geri-seçilecek id yok; canlı N11 listesi çekilip kayıtlı mahalle ADINA göre eşleştirilir (bulunamazsa
            // combo boş kalır ama isim adres modelinde korunur). Restore SESSİZ (EmitSelectionAsync YOK → adres ezilmez).
            if (_usesSubLocalityLevel)
            {
                await LoadNeighborhoodsAsync(localityId);
                if (!string.IsNullOrWhiteSpace(SelectedNeighborhoodName))
                {
                    // Son-ek-duyarsız eşleşme: kayıtlı "Oruçreis Mh." ↔ N11 listesindeki "Oruçreis Mah." varyantı eşlensin.
                    var target = NormalizeNeighborhoodName(SelectedNeighborhoodName);
                    _selectedNeighborhoodId = _neighborhoods
                        .FirstOrDefault(n => string.Equals(NormalizeNeighborhoodName(n.Name), target, StringComparison.OrdinalIgnoreCase))
                        ?.Id;
                }
            }
        }
    }

    // Başlangıç ülkesini çözer: FixedCountryId (sabit-ülke, öncelikli) → SelectedCountryCode (mevcut adres kodu) →
    // DefaultCountryId (varsayılan, adres boşken). IsDefault=true YALNIZ son yolda → çağıran LOUD restore uygular
    // (emit + cascade). İlk iki yol IsDefault=false (sessiz restore — mevcut adres alanları korunur). Hiçbiri yoksa null.
    private (CountryListDto? Country, bool IsDefault) ResolveInitialCountry()
    {
        if (FixedCountryId is { } fixedId)
        {
            return (_countries.FirstOrDefault(c => c.Id == fixedId), false);
        }

        if (!string.IsNullOrWhiteSpace(SelectedCountryCode))
        {
            return (_countries.FirstOrDefault(
                c => string.Equals(c.Code, SelectedCountryCode, StringComparison.OrdinalIgnoreCase)), false);
        }

        if (DefaultCountryId is { } defaultId)
        {
            return (_countries.FirstOrDefault(c => c.Id == defaultId), true);
        }

        return (null, false);
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
        await AutoSelectFirstFromAreasAsync();   // il→ilçe→mahalle ilk değerleri zincirleme iner (kullanıcı-tetikli)
        await EmitSelectionAsync();
    }

    // İl/eyalet değişti → ilçe + mahalleyi sıfırla, yeni alanın ilçelerini yükle, alt seviyeleri ilk değere indir, bildir.
    private async Task OnAreaSelectedAsync(Guid? areaId)
    {
        _selectedAreaId = areaId;
        ResetLocalityState();
        ResetNeighborhoodState();

        if (areaId is { } id)
        {
            await LoadLocalitiesAsync(id);
            await AutoSelectFirstLocalityChainAsync();   // ilk ilçe (+ mahalle seviyesi varsa ilk mahalle)
        }

        await EmitSelectionAsync();
    }

    // İlçe değişti → mahalleyi sıfırla; ülke mahalle seviyesi kullanıyorsa mahalleleri CANLI N11'den çek (saklamaz)
    // ve ilk mahalleyi otomatik seç. Seçimi bildir.
    private async Task OnLocalitySelectedAsync(Guid? localityId)
    {
        _selectedLocalityId = localityId;
        ResetNeighborhoodState();

        if (localityId is { } id && _usesSubLocalityLevel)
        {
            await LoadNeighborhoodsAsync(id);
            _selectedNeighborhoodId = _neighborhoods.Count > 0 ? _neighborhoods[0].Id : null;
        }

        await EmitSelectionAsync();
    }

    // ── Auto-select cascade (üst seviye seçilince alt seviyeler zincirleme İLK değerine iner) ──────────
    // Her yükleme AWAIT edilir → auto-select ancak API (lazy import / per-state şehir / canlı N11) BİTİNCE yapılır;
    // yükleme sürerken busy bayrakları combo'ları pasif tutar. Liste BOŞSA o seviyede durur (seçme). Kullanıcı bir üst
    // combo'yu değiştirince de (On*Selected) alttakiler yeniden ilk değere sıfırlanıp cascade iner.

    // LoadAreasAsync sonrası aşağı doğru cascade. Sembolik ülkede (il gizli) alan zaten seçili + ilçeler yüklü
    // (LoadAreasAsync yaptı) → ilk ilçeden devam; aksi halde ilk il'den başlayıp aşağı in.
    private async Task AutoSelectFirstFromAreasAsync()
    {
        if (_usesAreaLevel)
        {
            await AutoSelectFirstAreaChainAsync();
        }
        else
        {
            await AutoSelectFirstLocalityChainAsync();
        }
    }

    // İlk il'i seç → ilçelerini yükle → ilk ilçe zinciri. _areas boşsa seçme (il combo boş kalır).
    private async Task AutoSelectFirstAreaChainAsync()
    {
        if (_areas.Count == 0)
        {
            return;
        }

        _selectedAreaId = _areas[0].Id;
        await LoadLocalitiesAsync(_areas[0].Id);
        await AutoSelectFirstLocalityChainAsync();
    }

    // İlk ilçeyi seç → (ülke mahalle seviyesi kullanıyorsa) mahalleleri CANLI çek + ilk mahalleyi seç. Liste boşsa durur.
    private async Task AutoSelectFirstLocalityChainAsync()
    {
        if (_localities.Count == 0)
        {
            return;
        }

        _selectedLocalityId = _localities[0].Id;
        if (_usesSubLocalityLevel)
        {
            await LoadNeighborhoodsAsync(_localities[0].Id);
            _selectedNeighborhoodId = _neighborhoods.Count > 0 ? _neighborhoods[0].Id : null;
        }
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

    // Seçilen eyaletin ilçe/şehirlerini çeker. DB'de yoksa İLK seçimde per-state lazy import tetiklenir (birkaç sn —
    // US eyaleti için ~300 şehir dataset'ten süzülür); ikinci seçimde DB'den anında. İmport süresince combo pasif + hint.
    private async Task LoadLocalitiesAsync(Guid areaId)
    {
        _loadingLocalities = true;
        await InvokeAsync(StateHasChanged); // spinner/pasif combo hemen görünsün (per-state import öncesi)
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
        finally
        {
            _loadingLocalities = false;
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

    // Mahalle adını EŞLEŞME için sadeleştirir: Trim + sondaki "Mahallesi/Mah./Mh." son ekini atar. Kaynaklar farklı
    // yazar (sipariş adresi "Oruçreis Mh.", N11 canlı listesi "Oruçreis Mah.") — son-ek-duyarsız restore eşleşmesi için.
    private static string NormalizeNeighborhoodName(string value)
    {
        var trimmed = value.Trim();
        foreach (var suffix in new[] { "Mahallesi", "Mah.", "Mh.", "Mah", "Mh" })
        {
            if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                && trimmed.Length > suffix.Length)
            {
                return trimmed[..^suffix.Length].TrimEnd();
            }
        }

        return trimmed;
    }
}

/// <summary>Coğrafya cascade seçiminin anlık görüntüsü — ülke/il-eyalet/ilçe id + ad + kod (+ il ISO 3166-2 kodu),
/// mahalle ad + (canlı N11) id. Çağıran (ör. AddressFields) adres modelini doldurur: il → City, il kodu →
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
