using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.ProductCategories;
using Integration.TradeXpress.SalesChannels;
using DevExpress.Blazor;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.ProductCategories;

/// <summary>
/// ProductCategory dumb layout code-behind — Model bağlama, üst kategori seçimi, iç içe nitelik/değer drill
/// referansları, otomatik Sıra No ve dirty cascade.
/// </summary>
public partial class ProductCategoryLayout
{
    [Parameter, EditorRequired] public ProductCategoryGetDto Model { get; set; } = default!;

    /// <summary>Üst kategori seçenekleri (kendisi hariç; host doldurur).</summary>
    [Parameter] public List<ProductCategoryListDto> Categories { get; set; } = new();

    // Drill değişimini forma bildir (dirty/Save) — EntityEditForm EditChanged cascade'i.
    [CascadingParameter(Name = "EditChanged")] private Action? EditChanged { get; set; }

    private DrillList<ProductCategoryAttributeDto>? _attributeDrill;
    private DrillList<ProductCategoryAttributeValueDto>? _valueDrill;

    private List<AttributeKindItem> _kindItems = new();

    /// <summary>Üst kategori değişti — host sunucudan kalıtım önizlemesini alıp <c>Model.Attributes</c>'ı
    /// tazeler. Layout DUMB kalır (servis çağırmaz).</summary>
    [Parameter] public Func<Guid?, Task>? OnRefreshInheritedAttributes { get; set; }

    /// <summary>Kanal eşleştirme paneli gösterilsin mi. Host, YAZMA yolunun kullandığı kimliğin (rota Id)
    /// varlığına göre belirler — panelin görünürlüğü ile yazma hedefi AYNI kaynaktan gelmeli; ayrışırsa
    /// kullanıcı görünen panelde işlem yapar ama kayıt başka bir kategoriye gider ya da sessizce yutulur.</summary>
    [Parameter] public bool CanEditChannelMappings { get; set; }

    /// <summary>Bu kategorinin KENDİ kanal eşleştirmeleri (kanal başına en fazla bir satır) — host yükler,
    /// layout DUMB kalır.</summary>
    [Parameter] public List<ProductCategoryChannelMappingDto> ChannelMappings { get; set; } = new();

    /// <summary>Bir eşleştirme satırı kaydedildi — host sunucuya yazar ve çözülmüş komisyonla listeyi tazeler.</summary>
    [Parameter] public Func<ProductCategoryChannelMappingDto, Task>? OnSaveMapping { get; set; }

    /// <summary>Eşleştirme kaldırıldı — kategori o kanal için ATASININ eşleştirmesini devralmaya döner.</summary>
    [Parameter] public Func<SalesChannelType, Task>? OnRemoveMapping { get; set; }

    private DrillList<ProductCategoryChannelMappingDto>? _mappingDrill;

    protected override void OnInitialized()
    {
        _kindItems = Enum.GetValues<ProductCategoryAttributeKind>()
            .Select(kind => new AttributeKindItem(kind, KindText(kind)))
            .ToList();
    }

    /// <summary>Kanal değişince önceki kanalın kategori seçimi TAŞINMAZ — her pazaryerinin taksonomisi ayrı,
    /// N11 kategori kimliği Trendyol'da anlamsızdır.</summary>
    private void OnMappingChannelChanged(ProductCategoryChannelMappingDto mapping, SalesChannelType channel)
    {
        mapping.Channel = channel;
        mapping.ChannelCategoryExternalId = string.Empty;
        mapping.ChannelCategoryName = null;
        mapping.EffectiveCommissionRate = null;
    }

    private async Task OnChannelCategorySelected(
        ProductCategoryChannelMappingDto mapping, string externalId, string name)
    {
        mapping.ChannelCategoryExternalId = externalId;
        mapping.ChannelCategoryName = name;

        // Kanal kategorisi değişti → nitelik seçenekleri o kategoriye ait; host yeni listeyi çeker.
        // Önceki kategorinin nitelik SEÇİMLERİ de düşer: başka bir kategorinin nitelik kimliği burada
        // anlamsızdır ve sessizce yanlış alana yazılmasına yol açardı.
        foreach (var row in mapping.AttributeMappings)
        {
            row.ChannelAttributeExternalId = null;
            row.ChannelAttributeName = null;
        }

        if (OnChannelCategoryChanged is not null)
        {
            _requestedAttributeOptionsKey = (mapping.Channel, externalId);
            await OnChannelCategoryChanged(mapping.Channel, externalId);
            AutoMatchByName(mapping);
        }
    }

    /// <summary>Kanal kategorisi seçildi — host o kategorinin NİTELİK listesini çeker (layout DUMB kalır:
    /// üç pazaryerinin nitelik ucu ayrı servistedir).</summary>
    [Parameter] public Func<SalesChannelType, string, Task>? OnChannelCategoryChanged { get; set; }

    // Nitelik seçeneklerinin son istendiği (kanal + kanal kategorisi) — aynı hedef için tekrar istenmez.
    // Guard ŞART: istek render sırasında tetikleniyor, guard'sız her render yeni bir yükleme başlatır
    // (sonsuz döngü + gereksiz kanal API çağrısı).
    private (SalesChannelType Channel, string? ExternalId)? _requestedAttributeOptionsKey;

    /// <summary>Düzenlemeye açılan satırın kanal kategorisi için nitelik seçenekleri yüklü değilse yükletir.
    /// Render sırasında çağrılır; işin kendisi asenkron başlatılır ve bittiğinde host StateHasChanged eder.</summary>
    private void EnsureChannelAttributeOptions(ProductCategoryChannelMappingDto mapping)
    {
        if (OnChannelCategoryChanged is null || string.IsNullOrWhiteSpace(mapping.ChannelCategoryExternalId))
        {
            return;
        }

        var key = (mapping.Channel, mapping.ChannelCategoryExternalId);
        if (_requestedAttributeOptionsKey == key)
        {
            return;
        }

        _requestedAttributeOptionsKey = key;
        _ = InvokeAsync(async () =>
        {
            await OnChannelCategoryChanged(mapping.Channel, mapping.ChannelCategoryExternalId!);
            AutoMatchByName(mapping);
            StateHasChanged();
        });
    }

    /// <summary>Seçili kanal kategorisinin nitelikleri — eşleştirme combo'sunun kaynağı (host doldurur).</summary>
    [Parameter] public IReadOnlyList<ChannelAttributeOption> ChannelAttributeOptions { get; set; }
        = Array.Empty<ChannelAttributeOption>();

    /// <summary>Kanal niteliği seçildi — KİMLİK yazılır, ad yalnız gösterim için taşınır.</summary>
    private void OnChannelAttributePicked(ProductCategoryChannelAttributeMappingDto row, string? externalId)
    {
        row.ChannelAttributeExternalId = externalId;
        row.ChannelAttributeName = ChannelAttributeOptions
            .FirstOrDefault(o => o.ExternalId == externalId)?.Name;
    }

    /// <summary>Kanal DEĞER seçeneği — pazaryerinin değer listesindeki bir kayıt (kimlik + metin).</summary>
    public sealed record ChannelValueOption(string ExternalId, string Name);

    /// <summary>
    /// ADI BİREBİR AYNI olan nitelik ve değerleri otomatik eşleştirir — kullanıcı yalnız TUTMAYANLARLA uğraşsın.
    ///
    /// <para><b>Yalnız BOŞ satırlar doldurulur:</b> kullanıcının bilerek yaptığı bir seçim asla ezilmez
    /// (pazaryerinde aynı adlı iki nitelik olabilir ve kullanıcı kasten diğerini seçmiş olabilir).</para>
    ///
    /// <para><b>Neden yalnız BİREBİR eşleşme:</b> "içeren"/"benzeyen" eşleştirme sessizce yanlış alana yazar
    /// ("Ayar" ile "Ayar Belgesi" gibi) ve hata ancak pazaryeri ürünü reddedince fark edilirdi. Karşılaştırma
    /// kültür-duyarlı büyük/küçük harf farkını yok sayar (Türkçe "İ/ı" doğru eşleşsin).</para>
    ///
    /// <para>Eşleşme ÖNERİ değil doğrudan seçimdir: satırda görünür ve kullanıcı istediğini değiştirebilir.
    /// Ayrı bir "öneriyi kabul et" adımı, tutan yüzlerce satır için gereksiz tıklama olurdu.</para>
    /// </summary>
    private void AutoMatchByName(ProductCategoryChannelMappingDto mapping)
    {
        foreach (var row in mapping.AttributeMappings)
        {
            if (string.IsNullOrWhiteSpace(row.ChannelAttributeExternalId))
            {
                var match = ChannelAttributeOptions
                    .FirstOrDefault(o => NameMatches(o.Name, row.AttributeName));
                if (match is not null)
                {
                    row.ChannelAttributeExternalId = match.ExternalId;
                    row.ChannelAttributeName = match.Name;
                }
            }

            // Değerler ancak nitelik çözüldükten sonra eşleşebilir (değer listesi niteliğe ait).
            var valueOptions = ValueOptionsFor(row);
            if (valueOptions.Count == 0)
            {
                continue;
            }

            foreach (var value in row.ValueMappings.Where(v => string.IsNullOrWhiteSpace(v.ChannelValueExternalId)))
            {
                var match = valueOptions.FirstOrDefault(o => NameMatches(o.Name, value.ValueText));
                if (match is not null)
                {
                    value.ChannelValueExternalId = match.ExternalId;
                    value.ChannelValueName = match.Name;
                }
            }
        }
    }

    private static bool NameMatches(string? channelName, string? ownName)
    {
        return !string.IsNullOrWhiteSpace(channelName)
            && !string.IsNullOrWhiteSpace(ownName)
            && string.Equals(channelName.Trim(), ownName.Trim(), StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>Bir nitelik satırının kanal DEĞER seçenekleri — o satırda seçili kanal niteliğinin listesi.
    /// Nitelik eşleştirilmemişse BOŞTUR: hedef nitelik bilinmeden değer eşleştirmek anlamsızdır (hangi alana
    /// yazılacağı belirsiz kalır), sunucu da böyle satırları reddeder.</summary>
    private IReadOnlyList<ChannelValueOption> ValueOptionsFor(ProductCategoryChannelAttributeMappingDto row)
    {
        if (string.IsNullOrWhiteSpace(row.ChannelAttributeExternalId))
        {
            return Array.Empty<ChannelValueOption>();
        }

        return ChannelAttributeOptions
            .FirstOrDefault(o => o.ExternalId == row.ChannelAttributeExternalId)?.Values
            ?? Array.Empty<ChannelValueOption>();
    }

    /// <summary>Değer eşleştirme hücresi kaydedildi — düzenlenen satırı KİMLİĞİYLE bulup uygular.</summary>
    private void OnValueMappingSaving(
        ProductCategoryChannelAttributeMappingDto attribute, GridEditModelSavingEventArgs e)
    {
        if (e.EditModel is not ProductCategoryChannelAttributeValueMappingDto edited)
        {
            return;
        }

        var row = attribute.ValueMappings
            .FirstOrDefault(r => r.ProductCategoryAttributeValueId == edited.ProductCategoryAttributeValueId);
        if (row is null)
        {
            return;
        }

        row.ChannelValueExternalId = edited.ChannelValueExternalId;
        row.ChannelValueName = edited.ChannelValueName;
        EditChanged?.Invoke();
    }

    /// <summary>Kanal değeri seçildi — KİMLİK yazılır, ad yalnız gösterim için taşınır.</summary>
    private void OnChannelValuePicked(
        ProductCategoryChannelAttributeMappingDto attribute,
        ProductCategoryChannelAttributeValueMappingDto row,
        string? externalId)
    {
        row.ChannelValueExternalId = externalId;
        row.ChannelValueName = ValueOptionsFor(attribute)
            .FirstOrDefault(o => o.ExternalId == externalId)?.Name;
    }

    /// <summary>Nitelik eşleştirme hücresi kaydedildi — düzenlenen satırı KİMLİĞİYLE bulup uygular (grid
    /// düzenleme için satırın KOPYASINI verir; kopyayı listede bırakmak canlı satırı bayat gösterirdi).</summary>
    private void OnAttributeMappingSaving(ProductCategoryChannelMappingDto mapping, GridEditModelSavingEventArgs e)
    {
        if (e.EditModel is not ProductCategoryChannelAttributeMappingDto edited)
        {
            return;
        }

        var row = mapping.AttributeMappings
            .FirstOrDefault(r => r.ProductCategoryAttributeId == edited.ProductCategoryAttributeId);
        if (row is null)
        {
            return;
        }

        // Kanal NİTELİĞİ değiştiyse o niteliğe ait değer seçimleri DÜŞER: başka bir niteliğin değer kimliği
        // burada anlamsızdır ve sessizce yanlış değer gönderilmesine yol açardı.
        if (row.ChannelAttributeExternalId != edited.ChannelAttributeExternalId)
        {
            foreach (var value in row.ValueMappings)
            {
                value.ChannelValueExternalId = null;
                value.ChannelValueName = null;
            }
        }

        row.ChannelAttributeExternalId = edited.ChannelAttributeExternalId;
        row.ChannelAttributeName = edited.ChannelAttributeName;
        EditChanged?.Invoke();
    }

    /// <summary>
    /// Kanal nitelik seçeneği — üç pazaryerinin farklı DTO'ları tek şekle indirgenir (kimlik + ad + zorunluluk).
    ///
    /// <para><see cref="IsMandatory"/> yalnız sıralama/işaret için: pazaryerleri kategorilere ZORUNLU OLMAYAN
    /// mevzuat nitelikleri (N11'de GPSR/ürün güvenliği) ekliyor ve bunlar listede esas nitelikleri aşağı
    /// itiyordu — kullanıcı "Marka"yı bulmak için onlarca GPSR satırı arasında geziniyordu.</para>
    /// </summary>
    public sealed record ChannelAttributeOption(
        string ExternalId,
        string Name,
        bool IsMandatory,
        IReadOnlyList<ChannelValueOption> Values)
    {
        /// <summary>Combo'da görünen metin — zorunlular yıldızla işaretlenir (N11 ürün formuyla aynı gösterim).</summary>
        public string DisplayName
        {
            get { return IsMandatory ? Name + " *" : Name; }
        }
    }

    /// <summary>Kanal kategorisi seçilmeden satır kaydedilemez — boş eşleştirme, ürün fiyatlamasında "kanal
    /// kategorisi çözülemedi" diye sessizce komisyonsuz kalmaya yol açardı.</summary>
    private string? MappingSaveGuard(ProductCategoryChannelMappingDto mapping)
    {
        return string.IsNullOrWhiteSpace(mapping.ChannelCategoryExternalId)
            ? L["ProductCategory:ChannelCategoryRequired"].Value
            : null;
    }

    private async Task OnMappingSavedAsync(ProductCategoryChannelMappingDto mapping)
    {
        if (OnSaveMapping is not null)
        {
            await OnSaveMapping(mapping);
        }
    }

    /// <summary>Kanal adı — enum yerel karşılığı (ComboBoxEnumEdit ile aynı kaynak).</summary>
    private string ChannelText(SalesChannelType channel)
    {
        return L[$"Enum:{nameof(SalesChannelType)}:{channel}"].Value;
    }

    /// <summary>Komisyon gösterimi — çözülemediğinde sayı yerine sebebi anlatan metin (boş hücre kullanıcıya
    /// "sıfır komisyon" gibi görünürdü).</summary>
    private string CommissionText(decimal? rate)
    {
        return rate is { } value
            ? value.ToString("N2", CultureInfo.CurrentCulture) + " %"
            : L["ProductCategory:CommissionUnresolved"].Value;
    }

    /// <summary>Devralınan nitelik BURADA silinemez — sahibi üst kategoridedir. Silmeye izin verilseydi
    /// kullanıcı üst kategorinin tanımını sildiğini sanar, oysa hiçbir şey olmazdı (kaydetme zaten devralınan
    /// satırı yok sayar) ve satır bir sonraki açılışta geri gelirdi.</summary>
    private string? AttributeDeleteGuard(ProductCategoryAttributeDto attribute)
    {
        return attribute.IsInherited
            ? L["ProductCategory:InheritedAttributeNotDeletable", attribute.SourceCategoryName ?? string.Empty].Value
            : null;
    }

    /// <summary>Devralınan DEĞER burada silinemez (nitelikteki gerekçenin aynısı); kullanıcı kendi değerlerini
    /// serbestçe ekler/siler.</summary>
    private string? ValueDeleteGuard(ProductCategoryAttributeValueDto value)
    {
        return value.IsInherited
            ? L["ProductCategory:InheritedValueNotDeletable", value.SourceCategoryName ?? string.Empty].Value
            : null;
    }

    /// <summary>Kaynak kolonu — devralınanda üst kategorinin adı, kendi satırında boş (gürültü yapmasın).</summary>
    private static string AttributeSourceText(ProductCategoryAttributeDto attribute)
    {
        return attribute.IsInherited ? attribute.SourceCategoryName ?? string.Empty : string.Empty;
    }

    private static string ValueSourceText(ProductCategoryAttributeValueDto value)
    {
        return value.IsInherited ? value.SourceCategoryName ?? string.Empty : string.Empty;
    }

    /// <summary>Üst kategori seçimi — grid'i ANINDA tazeler: yeni üstün (ve onun TÜM atalarının) nitelikleri
    /// devralınan olarak görünür, kullanıcının kendi satırları korunur. Birleştirmeyi sunucu yapar (tek kaynak).</summary>
    private async Task OnParentChanged(Guid? parentId)
    {
        Model.ParentId = parentId;

        if (OnRefreshInheritedAttributes is not null)
        {
            await OnRefreshInheritedAttributes(parentId);
        }

        EditChanged?.Invoke();
    }

    private string KindText(ProductCategoryAttributeKind kind)
    {
        return L[$"ProductCategoryAttributeKind:{kind}"].Value;
    }



    // Yeni nitelik eklenince Sıra No OTOMATİK artar (max + 1; boşsa 1).
    private int NextAttributeOrder()
    {
        return Model.Attributes.Select(x => x.DisplayOrder).DefaultIfEmpty(0).Max() + 1;
    }

    // Yeni değer eklenince Sıra No OTOMATİK artar (nitelik içi max + 1; boşsa 1).
    private static int NextValueOrder(ProductCategoryAttributeDto attribute)
    {
        return attribute.Values.Select(x => x.DisplayOrder).DefaultIfEmpty(0).Max() + 1;
    }

    /// <summary>Nitelik cinsi combo satırı (enum + lokalize ad).</summary>
    public sealed record AttributeKindItem(ProductCategoryAttributeKind Value, string Text);
}
