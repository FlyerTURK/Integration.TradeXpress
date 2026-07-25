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

    /// <summary>Geri yükleme — kayıtlı il/eyalet ADI. Yalnız <see cref="SelectedAdministrativeAreaId"/> katalogda
    /// BULUNAMADIĞINDA kullanılır: adres serbest metinle girilmişse (katalog boşken ya da listede olmayan yer)
    /// id yoktur, ad vardır — combo metni bundan geri yüklenir, aksi halde kullanıcının yazdığı il kaybolmuş görünür.</summary>
    [Parameter] public string? SelectedAdministrativeAreaName { get; set; }

    /// <summary>Geri yükleme — seçili yerellik id'si (çağıranda varsa).</summary>
    [Parameter] public Guid? SelectedLocalityId { get; set; }

    /// <summary>Geri yükleme — kayıtlı ilçe ADI (serbest metin yolu; bkz. <see cref="SelectedAdministrativeAreaName"/>).</summary>
    [Parameter] public string? SelectedLocalityName { get; set; }

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

    // ── SERBEST METİN (AllowUserInput) ──────────────────────────────────────────────────────────────
    // Katalog her adresi kapsayamaz: N11 senkronu yapılmadan ilçe/mahalle listesi BOŞTUR (ilk kurulum),
    // ayrıca yeni/atlanmış ilçe ile yurtdışı adresleri hiçbir zaman listede olmayabilir. Bu yüzden üç combo da
    // listede OLMAYAN değer kabul eder: ad SAKLANIR, id BOŞ kalır. Adres VO'su buna uygun — City/District/
    // Neighborhood string, AdministrativeAreaId/LocalityId nullable.
    // NOT: DevExpress TextChanged listeden SEÇİMDE de tetiklenir → handler'lar önce listeyle eşleşmeye bakar,
    // eşleşiyorsa ValueChanged yolunu EZMEZ.
    private string? _areaText;
    private string? _localityText;
    private string? _neighborhoodText;

    // Alt seviyenin açık olması için üstte ya SEÇİM ya da elle YAZILMIŞ bir değer yeterlidir (aksi halde
    // katalog boşken kullanıcı il yazıp ilçeye geçemezdi).
    private bool HasAreaContext => _selectedAreaId != null || !string.IsNullOrWhiteSpace(_areaText);
    private bool HasLocalityContext => _selectedLocalityId != null || !string.IsNullOrWhiteSpace(_localityText);

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

        // BOŞ FORM → LOUD: alt seviyeleri ilk değerine indir + seçimi çağırana BİLDİR (adres modeli ülke +
        // ilk il/ilçe/mahalle ile dolar). Mevcut adres varsa buraya GİRİLMEZ — restore sessizdir, kayıtlı adres EZİLMEZ.
        // İki yol da boş formdur:
        //   · isDefault  → ülke DefaultCountryId'den ön-seçildi (adreste ülke kodu bile yoktu).
        //   · blank      → ülke FixedCountryId ile KİLİTLİ (ör. şube adresi company ülkesine kilitli) ama hiçbir
        //                  coğrafya seviyesi kayıtlı değil. Eskiden yalnız isDefault yolu cascade'i indirirdi;
        //                  kilitli-ülke yolunda İl/İlçe/Mahalle BOŞ açılıyordu (2026-07-25 Hakan bulgusu).
        if (isDefault || IsBlankAddress())
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
            _areaText = _areas.First(a => a.Id == areaId).Name;
            await LoadLocalitiesAsync(areaId);
        }
        else if (_usesAreaLevel)
        {
            // Id YOK ama ad olabilir (serbest metinle girilmiş adres) → combo metnini kayıtlı addan geri yükle,
            // yoksa kullanıcı formu açtığında yazdığı il kaybolmuş gibi görünürdü.
            _areaText = Trimmed(SelectedAdministrativeAreaName);
        }

        // Yerellik geri yükleme.
        if (SelectedLocalityId is { } localityId && _localities.Any(l => l.Id == localityId))
        {
            _selectedLocalityId = localityId;
            _localityText = _localities.First(l => l.Id == localityId).Name;

            // GÜNCELLEME YOLU — mahalle listesi çekilir ama İLK DEĞER SEÇİLMEZ: kayıtlı adres KENDİ mahallesini
            // gösterir (2026-07-25 Hakan kararı: "updatede hangi kayıtsa o gösterilsin"). Mahalle SAKLANMADIĞINDAN
            // geri-seçilecek id yok → canlı liste kayıtlı ADA göre eşleştirilir; eşleşme yoksa combo metni yine
            // kayıtlı adı taşır (aşağıda _neighborhoodText) ve adres modeli bozulmaz. Restore SESSİZ (emit YOK).
            if (_usesSubLocalityLevel)
            {
                await LoadNeighborhoodsAsync(localityId);
                if (!string.IsNullOrWhiteSpace(SelectedNeighborhoodName))
                {
                    // Son-ek duyarsız: kayıtlı "Oruçreis Mh." ↔ N11 listesindeki "Oruçreis Mah." eşleşsin.
                    var target = NormalizeNeighborhoodName(SelectedNeighborhoodName);
                    _selectedNeighborhoodId = _neighborhoods
                        .FirstOrDefault(n => string.Equals(NormalizeNeighborhoodName(n.Name), target, StringComparison.OrdinalIgnoreCase))
                        ?.Id;
                }
            }
        }
        else
        {
            // Id yok — ilçe serbest metinle girilmiş olabilir; combo metnini kayıtlı addan geri yükle.
            _localityText = Trimmed(SelectedLocalityName);
        }

        // Mahalle metni her hâlükârda kayıtlı addan gelir (mahalle SAKLANMAZ; eşleşme bulunduysa combo da seçili).
        _neighborhoodText = Trimmed(SelectedNeighborhoodName);
    }

    /// <summary>Adreste HİÇBİR coğrafya seviyesi kayıtlı değil mi? (id de yok, serbest-metin ad da yok.) Boş form
    /// demektir → alt seviyeler ilk değerine indirilir. Tek bir seviye bile doluysa FALSE döner ve restore sessiz
    /// kalır — aksi halde kayıtlı bir adres açıldığında eksik seviyeler sessizce uydurulurdu.</summary>
    private bool IsBlankAddress()
    {
        return SelectedAdministrativeAreaId is null
               && string.IsNullOrWhiteSpace(SelectedAdministrativeAreaName)
               && SelectedLocalityId is null
               && string.IsNullOrWhiteSpace(SelectedLocalityName)
               && string.IsNullOrWhiteSpace(SelectedNeighborhoodName);
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
        _areaText = _areas.FirstOrDefault(a => a.Id == areaId)?.Name;   // combo metnini seçimle senkron tut
        _localityText = null;
        _neighborhoodText = null;
        ResetLocalityState();
        ResetNeighborhoodState();

        if (areaId is { } id)
        {
            await LoadLocalitiesAsync(id);
            await AutoSelectFirstLocalityChainAsync();   // ilk ilçe + mahalle (ilçe değişti → çek + ilk değer)
        }

        await EmitSelectionAsync();
    }

    // İlçe değişti → mahalleyi sıfırla; ülke mahalle seviyesi kullanıyorsa mahalleleri CANLI N11'den çek (saklamaz)
    // ve ilk mahalleyi otomatik seç. Seçimi bildir.
    private async Task OnLocalitySelectedAsync(Guid? localityId)
    {
        _selectedLocalityId = localityId;
        _localityText = _localities.FirstOrDefault(l => l.Id == localityId)?.Name;
        _neighborhoodText = null;
        ResetNeighborhoodState();

        // İLÇE DEĞİŞTİ → mahalle listesi API'den ÇEKİLİR ve İLK değer seçilir. Yeni ilçenin altında eski mahalle
        // geçersiz olduğundan (güncelleme modunda bile) burada ilk değer doğru davranıştır.
        if (localityId is { } id && _usesSubLocalityLevel)
        {
            await LoadNeighborhoodsAsync(id);
            _selectedNeighborhoodId = _neighborhoods.Count > 0 ? _neighborhoods[0].Id : null;
            _neighborhoodText = _neighborhoods.Count > 0 ? _neighborhoods[0].Name : null;
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

    // İlk il'i seç → ilçelerini yükle → ilk ilçeyi seç. _areas boşsa seçme (il combo boş kalır).
    private async Task AutoSelectFirstAreaChainAsync()
    {
        if (_areas.Count == 0)
        {
            return;
        }

        _selectedAreaId = _areas[0].Id;
        _areaText = _areas[0].Name;   // Text tek-yön bağlı: ad yazılmazsa combo Value dolu ama BOŞ görünür
        await LoadLocalitiesAsync(_areas[0].Id);
        await AutoSelectFirstLocalityChainAsync();
    }

    // İlk ilçeyi seç → mahalleyi API'den çek → ilk mahalleyi seç (YENİ kayıt yolu; boş formda çalışır).
    // Tetik "ilçe DEĞİŞTİ"dir; ister kullanıcı seçsin ister sistem cascade'i (2026-07-25 Hakan kararı).
    // GÜNCELLEME yolu buraya GİRMEZ — kayıtlı adres RestoreSelectionAsync'te kendi mahallesini gösterir.
    private async Task AutoSelectFirstLocalityChainAsync()
    {
        if (_localities.Count == 0)
        {
            return;
        }

        _selectedLocalityId = _localities[0].Id;
        _localityText = _localities[0].Name;   // Text tek-yön bağlı: ad yazılmazsa combo Value dolu ama BOŞ görünür

        if (!_usesSubLocalityLevel)
        {
            return;
        }

        await LoadNeighborhoodsAsync(_localities[0].Id);
        _selectedNeighborhoodId = _neighborhoods.Count > 0 ? _neighborhoods[0].Id : null;
        _neighborhoodText = _neighborhoods.Count > 0 ? _neighborhoods[0].Name : null;
    }

    // Mahalle değişti → seçimi bildir.
    private async Task OnNeighborhoodSelectedAsync(string? neighborhoodId)
    {
        _selectedNeighborhoodId = neighborhoodId;
        _neighborhoodText = _neighborhoods.FirstOrDefault(n => n.Id == neighborhoodId)?.Name;
        await EmitSelectionAsync();
    }

    // ── Serbest metin girişleri (listede OLMAYAN değer) ──────────────────────────────────────────────
    // Ortak kural: metin listedeki bir adla eşleşiyorsa ValueChanged zaten işledi → DOKUNMA. Eşleşmiyorsa
    // ÖZEL değerdir: id'yi boşalt (ad taşınır), alt seviyeleri sıfırla (üst değişti) ve seçimi bildir.

    private async Task OnAreaTextChangedAsync(string? text)
    {
        if (MatchesSelection(text, _areas.FirstOrDefault(a => a.Id == _selectedAreaId)?.Name))
        {
            return;
        }

        _areaText = text;
        _selectedAreaId = null;      // özel değer → çekirdek coğrafya id'si YOK (ad serbest metin olarak taşınır)
        ResetLocalityState();
        ResetNeighborhoodState();
        _localityText = null;
        _neighborhoodText = null;
        await EmitSelectionAsync();
    }

    private async Task OnLocalityTextChangedAsync(string? text)
    {
        if (MatchesSelection(text, _localities.FirstOrDefault(l => l.Id == _selectedLocalityId)?.Name))
        {
            return;
        }

        _localityText = text;
        _selectedLocalityId = null;
        ResetNeighborhoodState();
        _neighborhoodText = null;
        await EmitSelectionAsync();
    }

    private async Task OnNeighborhoodTextChangedAsync(string? text)
    {
        if (MatchesSelection(text, _neighborhoods.FirstOrDefault(n => n.Id == _selectedNeighborhoodId)?.Name))
        {
            return;
        }

        _neighborhoodText = text;
        _selectedNeighborhoodId = null;
        await EmitSelectionAsync();
    }

    /// <summary>Metin, hâlihazırda SEÇİLİ öğenin adıyla aynı mı? (DevExpress TextChanged'i listeden seçimde de
    /// tetikler — o durumda ValueChanged yolu doğru değeri zaten kurdu, serbest-metin yolu araya girmemeli.)</summary>
    private static bool MatchesSelection(string? text, string? selectedName)
    {
        return selectedName != null && string.Equals(text?.Trim(), selectedName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Serbest metni adres modeline uygun hâle getirir: kırp, boşsa null (boş string yazma).</summary>
    private static string? Trimmed(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
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

        // SERBEST METİN GERİ DÜŞÜŞÜ: seçili öğe yoksa kullanıcının YAZDIĞI ad taşınır (id null kalır) — katalog
        // boşken (N11 senkronu öncesi) ya da adres listede olmayan bir yerdeyken adres yine de tam girilebilsin.
        var areaName = _usesAreaLevel ? (area?.Name ?? Trimmed(_areaText)) : null;
        var areaCode = _usesAreaLevel ? area?.Code : null;
        var areaIsoCode = _usesAreaLevel ? area?.Iso3166_2Code : null;

        // Mahalle seviyesi kullanılmayan ülkede mahalle ad/id'si BİLDİRİLMEZ (combo gizli — Neighborhood doldurulmaz).
        // Mahalle SAKLANMADIĞINDAN yalnız ad (ve canlı N11 id'si) taşınır; adres yalnız adı serbest-metin tutar.
        var neighborhoodId = _usesSubLocalityLevel ? neighborhood?.Id : null;
        var neighborhoodName = _usesSubLocalityLevel ? (neighborhood?.Name ?? Trimmed(_neighborhoodText)) : null;

        await SelectionChanged.InvokeAsync(new GeographySelection(
            CountryId: country.Id,
            CountryCode: country.Code,
            CountryName: country.Name,
            AdministrativeAreaId: area?.Id,
            AdministrativeAreaName: areaName,
            AdministrativeAreaCode: areaCode,
            AdministrativeAreaIsoCode: areaIsoCode,
            LocalityId: locality?.Id,
            LocalityName: locality?.Name ?? Trimmed(_localityText),
            LocalityCode: locality?.Code,
            NeighborhoodId: neighborhoodId,
            NeighborhoodName: neighborhoodName,
            UsesSubLocality: _usesSubLocalityLevel));
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
    string CountryName,
    Guid? AdministrativeAreaId,
    string? AdministrativeAreaName,
    string? AdministrativeAreaCode,
    string? AdministrativeAreaIsoCode,
    Guid? LocalityId,
    string? LocalityName,
    string? LocalityCode,
    string? NeighborhoodId,
    string? NeighborhoodName,
    bool UsesSubLocality);
