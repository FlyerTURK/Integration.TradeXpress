using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.Orders;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels.Etsy;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.EtsyProducts;

/// <summary>
/// <see cref="IEtsyProductClient"/> — Etsy Open API v3 listeleme OKUMA istemcisi (<see cref="EtsyOrderClient"/> ile AYNI
/// auth/paginasyon şablonu). Salt GET (<c>getListingsByShop</c> active + <c>includes=Inventory,Images</c>). İnventory/
/// görsel tek çağrıda gelmezse per-listing fallback (<c>getListingInventory</c> / <c>getListingImages</c>). Para
/// tutarları Etsy money-object (<c>{amount,divisor}</c>) → decimal. Defansif JSON okuma (alan yoksa/tipi farklıysa null).
/// </summary>
public sealed class EtsyProductClient : IEtsyProductClient, ITransientDependency
{
    // Listeleme çekimi seyrek → paylaşılan tek HttpClient (OAuth/sipariş istemcileriyle aynı desen).
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    private const int MaxPageLoops = 500;   // offset döngüsü güvenlik tavanı (bozuk count → sonsuz döngü olmasın)

    private readonly IEtsyTokenProvider _tokenProvider;

    public EtsyProductClient(IEtsyTokenProvider tokenProvider)
    {
        _tokenProvider = tokenProvider;
    }

    public async Task<IReadOnlyList<EtsyRemoteListing>> GetAllListingsAsync(
        EtsyCredentials credentials, int pageSize = 100, CancellationToken cancellationToken = default)
    {
        var accessToken = await _tokenProvider.GetAccessTokenAsync(credentials.ChannelId, cancellationToken);

        var all = new List<EtsyRemoteListing>();
        var offset = 0;
        var loops = 0;
        int count;
        do
        {
            var (items, total) = await GetListingsPageAsync(credentials, accessToken, offset, pageSize, cancellationToken);
            all.AddRange(items);
            count = total;
            offset += pageSize;
            loops++;
        }
        while (offset < count && loops < MaxPageLoops);

        if (offset < count)
        {
            throw new BusinessException("TradeXpress:Etsy:Product:ListingPageLimitExceeded")
                .WithData("count", count)
                .WithData("maxPages", MaxPageLoops);
        }

        // İnventory/görsel includes ile gelmediyse per-listing fallback ile tamamla (defansif — canlı doğrulanacak).
        for (var i = 0; i < all.Count; i++)
        {
            var listing = all[i];
            var offerings = listing.Offerings;
            if (offerings.Count == 0)
            {
                // Inventory alt-çağrısı başarısızsa inline (boş) ile devam — offering'siz listeleme import'ta atlanır (sessiz değil).
                try { offerings = await GetListingInventoryAsync(credentials, accessToken, listing.ListingId, cancellationToken); }
                catch (BusinessException) { offerings = listing.Offerings; }
            }

            var imageUrls = listing.ImageUrls;
            if (imageUrls.Count == 0)
            {
                // GÖRSEL OPSİYONEL — alt-çağrı başarısızsa boş; TÜM import bu yüzden DÜŞMEZ (round-trip'in çekirdeği ürün/varyant).
                try { imageUrls = await GetListingImagesAsync(credentials, accessToken, listing.ListingId, cancellationToken); }
                catch (BusinessException) { imageUrls = listing.ImageUrls; }
            }

            all[i] = listing with { Offerings = offerings, ImageUrls = imageUrls };
        }

        return all;
    }

    public async Task<IReadOnlyList<EtsyShippingProfileSummary>> GetShopShippingProfilesAsync(
        EtsyCredentials credentials, CancellationToken cancellationToken = default)
    {
        var accessToken = await _tokenProvider.GetAccessTokenAsync(credentials.ChannelId, cancellationToken);
        var url = $"{EtsyOAuthConsts.ApiBaseUrl}/application/shops/{Uri.EscapeDataString(credentials.ShopId)}/shipping-profiles";
        var payload = await SendGetAsync(url, credentials.ApiKeyHeader, accessToken, cancellationToken, "TradeXpress:Etsy:Product:ShippingProfilesFailed");
        return ParseShippingProfiles(payload);
    }

    public async Task<IReadOnlyList<EtsyReturnPolicySummary>> GetShopReturnPoliciesAsync(
        EtsyCredentials credentials, CancellationToken cancellationToken = default)
    {
        var accessToken = await _tokenProvider.GetAccessTokenAsync(credentials.ChannelId, cancellationToken);
        var url = $"{EtsyOAuthConsts.ApiBaseUrl}/application/shops/{Uri.EscapeDataString(credentials.ShopId)}/policies/return";
        var payload = await SendGetAsync(url, credentials.ApiKeyHeader, accessToken, cancellationToken, "TradeXpress:Etsy:Product:ReturnPoliciesFailed");
        return ParseReturnPolicies(payload);
    }

    public async Task<IReadOnlyList<EtsyShopSectionSummary>> GetShopSectionsAsync(
        EtsyCredentials credentials, CancellationToken cancellationToken = default)
    {
        var accessToken = await _tokenProvider.GetAccessTokenAsync(credentials.ChannelId, cancellationToken);
        var url = $"{EtsyOAuthConsts.ApiBaseUrl}/application/shops/{Uri.EscapeDataString(credentials.ShopId)}/sections";
        var payload = await SendGetAsync(url, credentials.ApiKeyHeader, accessToken, cancellationToken, "TradeXpress:Etsy:Product:ShopSectionsFailed");
        return ParseShopSections(payload);
    }

    public async Task<EtsyShopSectionSummary> CreateShopSectionAsync(
        EtsyCredentials credentials, string title, CancellationToken cancellationToken = default)
    {
        var accessToken = await _tokenProvider.GetAccessTokenAsync(credentials.ChannelId, cancellationToken);
        var url = $"{EtsyOAuthConsts.ApiBaseUrl}/application/shops/{Uri.EscapeDataString(credentials.ShopId)}/sections";
        var form = new Dictionary<string, string> { ["title"] = title };
        var payload = await SendFormAsync(HttpMethod.Post, url, form, credentials.ApiKeyHeader, accessToken, cancellationToken, "TradeXpress:Etsy:Product:ShopSectionCreateFailed");
        return ParseShopSection(payload);
    }

    public async Task<EtsyShopSectionSummary> UpdateShopSectionAsync(
        EtsyCredentials credentials, long shopSectionId, string title, CancellationToken cancellationToken = default)
    {
        var accessToken = await _tokenProvider.GetAccessTokenAsync(credentials.ChannelId, cancellationToken);
        var url = $"{EtsyOAuthConsts.ApiBaseUrl}/application/shops/{Uri.EscapeDataString(credentials.ShopId)}/sections/{shopSectionId}";
        var form = new Dictionary<string, string> { ["title"] = title };
        var payload = await SendFormAsync(HttpMethod.Put, url, form, credentials.ApiKeyHeader, accessToken, cancellationToken, "TradeXpress:Etsy:Product:ShopSectionUpdateFailed");
        return ParseShopSection(payload);
    }

    public async Task<EtsyReturnPolicySummary> CreateReturnPolicyAsync(
        EtsyCredentials credentials, bool acceptsReturns, bool acceptsExchanges, int? returnDeadlineDays,
        CancellationToken cancellationToken = default)
    {
        var accessToken = await _tokenProvider.GetAccessTokenAsync(credentials.ChannelId, cancellationToken);
        var url = $"{EtsyOAuthConsts.ApiBaseUrl}/application/shops/{Uri.EscapeDataString(credentials.ShopId)}/policies/return";
        var form = BuildReturnPolicyForm(acceptsReturns, acceptsExchanges, returnDeadlineDays);
        var payload = await SendFormAsync(HttpMethod.Post, url, form, credentials.ApiKeyHeader, accessToken, cancellationToken, "TradeXpress:Etsy:Product:ReturnPolicyCreateFailed");
        return ParseReturnPolicy(payload);
    }

    public async Task<EtsyReturnPolicySummary> UpdateReturnPolicyAsync(
        EtsyCredentials credentials, long returnPolicyId, bool acceptsReturns, bool acceptsExchanges, int? returnDeadlineDays,
        CancellationToken cancellationToken = default)
    {
        var accessToken = await _tokenProvider.GetAccessTokenAsync(credentials.ChannelId, cancellationToken);
        var url = $"{EtsyOAuthConsts.ApiBaseUrl}/application/shops/{Uri.EscapeDataString(credentials.ShopId)}/policies/return/{returnPolicyId}";
        var form = BuildReturnPolicyForm(acceptsReturns, acceptsExchanges, returnDeadlineDays);
        var payload = await SendFormAsync(HttpMethod.Put, url, form, credentials.ApiKeyHeader, accessToken, cancellationToken, "TradeXpress:Etsy:Product:ReturnPolicyUpdateFailed");
        return ParseReturnPolicy(payload);
    }

    // İade politikası form-urlencoded gövdesi — Etsy accepts_returns/accepts_exchanges'i bool string (lowercase) bekler;
    // return_deadline yalnız KABUL varsa (iade ya da değişim) + değer verildiyse gönderilir (aksi halde Etsy reddeder).
    private static Dictionary<string, string> BuildReturnPolicyForm(bool acceptsReturns, bool acceptsExchanges, int? returnDeadlineDays)
    {
        var form = new Dictionary<string, string>
        {
            ["accepts_returns"] = acceptsReturns ? "true" : "false",
            ["accepts_exchanges"] = acceptsExchanges ? "true" : "false",
        };
        if ((acceptsReturns || acceptsExchanges) && returnDeadlineDays is { } days)
        {
            form["return_deadline"] = days.ToString(CultureInfo.InvariantCulture);
        }

        return form;
    }

    public async Task<EtsyIdentity?> VerifyIdentityAsync(
        EtsyCredentials credentials, CancellationToken cancellationToken = default)
    {
        var accessToken = await _tokenProvider.GetAccessTokenAsync(credentials.ChannelId, cancellationToken);
        // getMe shop segmenti ALMAZ — token'ın çözdüğü kullanıcı/mağazayı döner (kimlik ön-koşul teyidi).
        var url = $"{EtsyOAuthConsts.ApiBaseUrl}/application/users/me";
        var payload = await SendGetAsync(url, credentials.ApiKeyHeader, accessToken, cancellationToken, "TradeXpress:Etsy:Product:IdentityFailed");
        return ParseIdentity(payload);
    }

    // ── Sayfa çekimi ────────────────────────────────────────────────────────────────────────────────

    private async Task<(List<EtsyRemoteListing> Items, int Count)> GetListingsPageAsync(
        EtsyCredentials credentials, string accessToken, int offset, int limit, CancellationToken cancellationToken)
    {
        var url = $"{EtsyOAuthConsts.ApiBaseUrl}/application/shops/{Uri.EscapeDataString(credentials.ShopId)}/listings/active" +
                  $"?limit={limit}&offset={offset}&includes=Inventory,Images";
        var payload = await SendGetAsync(url, credentials.ApiKeyHeader, accessToken, cancellationToken, "TradeXpress:Etsy:Product:ListingListFailed");
        return ParseListingsPage(payload);
    }

    private async Task<IReadOnlyList<EtsyRemoteOffering>> GetListingInventoryAsync(
        EtsyCredentials credentials, string accessToken, long listingId, CancellationToken cancellationToken)
    {
        var url = $"{EtsyOAuthConsts.ApiBaseUrl}/application/listings/{listingId}/inventory";
        var payload = await SendGetAsync(url, credentials.ApiKeyHeader, accessToken, cancellationToken, "TradeXpress:Etsy:Product:InventoryFailed");
        try
        {
            using var doc = JsonDocument.Parse(payload);
            return ReadOfferings(doc.RootElement, out _);
        }
        catch (JsonException)
        {
            throw new BusinessException("TradeXpress:Etsy:Product:ListingListParseFailed");
        }
    }

    private async Task<IReadOnlyList<string>> GetListingImagesAsync(
        EtsyCredentials credentials, string accessToken, long listingId, CancellationToken cancellationToken)
    {
        // Etsy getListingImages shop segmenti ALMAZ (shop-path'te 404 "Resource not found" — canlı doğrulandı 2026-07-19).
        var url = $"{EtsyOAuthConsts.ApiBaseUrl}/application/listings/{listingId}/images";
        var payload = await SendGetAsync(url, credentials.ApiKeyHeader, accessToken, cancellationToken, "TradeXpress:Etsy:Product:ImagesFailed");
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var result = new List<string>();
            if (doc.RootElement.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                foreach (var img in results.EnumerateArray())
                {
                    var imgUrl = ReadImageUrl(img);
                    if (!string.IsNullOrWhiteSpace(imgUrl))
                    {
                        result.Add(imgUrl!);
                    }
                }
            }

            return result;
        }
        catch (JsonException)
        {
            throw new BusinessException("TradeXpress:Etsy:Product:ListingListParseFailed");
        }
    }

    private static async Task<string> SendGetAsync(
        string url, string apiKeyHeader, string accessToken, CancellationToken cancellationToken, string failureCode)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        // Etsy v3 HER istekte x-api-key (app keystring) İSTER — Bearer'a EK. Eksikse 401/403 (order client ile aynı desen).
        request.Headers.TryAddWithoutValidation("x-api-key", apiKeyHeader);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new BusinessException(failureCode, $"Etsy {failureCode} → HTTP {(int)response.StatusCode}: {Truncate(payload)}")
                .WithData("status", (int)response.StatusCode)
                .WithData("body", Truncate(payload));
        }

        return payload;
    }

    // Etsy write ucu (POST/PUT) — gövde application/x-www-form-urlencoded (Etsy v3 write'ları JSON DEĞİL form bekler).
    // GET ile AYNI kimlik: her istekte x-api-key + Bearer. Başarısız → dostane BusinessException (gövde kırpılı; UI toast).
    private static async Task<string> SendFormAsync(
        HttpMethod method, string url, IReadOnlyDictionary<string, string> form, string apiKeyHeader, string accessToken,
        CancellationToken cancellationToken, string failureCode)
    {
        using var request = new HttpRequestMessage(method, url)
        {
            Content = new FormUrlEncodedContent(form),
        };
        request.Headers.TryAddWithoutValidation("x-api-key", apiKeyHeader);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new BusinessException(failureCode, $"Etsy {failureCode} → HTTP {(int)response.StatusCode}: {Truncate(payload)}")
                .WithData("status", (int)response.StatusCode)
                .WithData("body", Truncate(payload));
        }

        return payload;
    }

    // ── Parse ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Listeleme sayfa yanıtını parse eder (public static — birim testli): <c>count</c> + <c>results[]</c>.
    /// Bozuk JSON gövdesi dostane hatayla ÇEKİMİ DURDURUR (sessizce boş sayfa dönmek sayfaları raporsuz kaybettirirdi).</summary>
    public static (List<EtsyRemoteListing> Items, int Count) ParseListingsPage(string payload)
    {
        var items = new List<EtsyRemoteListing>();
        int count;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            count = ReadInt(root, "count") ?? 0;

            if (root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in results.EnumerateArray())
                {
                    items.Add(ReadListing(el));
                }
            }
        }
        catch (JsonException)
        {
            throw new BusinessException("TradeXpress:Etsy:Product:ListingListParseFailed");
        }

        return (items, count);
    }

    /// <summary>Kargo profili yanıtını parse eder (public static — birim testli): <c>results[]</c> → (shipping_profile_id,
    /// title). Silinmiş profil (<c>is_deleted=true</c>) + kimliksiz/başlıksız öğe elenir. Bozuk JSON gövdesi dostane
    /// hatayla DURDURUR (sessizce boş dönmek "mağazada profil yok" ile karışırdı).</summary>
    public static IReadOnlyList<EtsyShippingProfileSummary> ParseShippingProfiles(string payload)
    {
        var result = new List<EtsyShippingProfileSummary>();
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in results.EnumerateArray())
                {
                    if (ReadBool(el, "is_deleted") == true)
                    {
                        continue;   // silinmiş profil picker'a girmez
                    }

                    var id = ReadLong(el, "shipping_profile_id");
                    var title = ReadString(el, "title");
                    if (id is > 0 && !string.IsNullOrWhiteSpace(title))
                    {
                        result.Add(new EtsyShippingProfileSummary(id.Value, title!.Trim()));
                    }
                }
            }
        }
        catch (JsonException)
        {
            throw new BusinessException("TradeXpress:Etsy:Product:ShippingProfilesParseFailed");
        }

        return result;
    }

    /// <summary>İade politikası yanıtını parse eder (public static — birim testli): <c>results[]</c> → (return_policy_id,
    /// return_deadline, accepts_returns, accepts_exchanges). Etsy iade politikasının BAŞLIĞI YOKTUR → yalnız ham alanlar
    /// döner, görüntü etiketi AppService'te lokalize türetilir. Kimliksiz öğe elenir. Bozuk JSON gövdesi dostane hatayla
    /// DURDURUR (sessizce boş dönmek "mağazada politika yok" ile karışırdı — kargo profili deseniyle aynı).</summary>
    public static IReadOnlyList<EtsyReturnPolicySummary> ParseReturnPolicies(string payload)
    {
        var result = new List<EtsyReturnPolicySummary>();
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in results.EnumerateArray())
                {
                    var id = ReadLong(el, "return_policy_id");
                    if (id is > 0)
                    {
                        result.Add(new EtsyReturnPolicySummary(
                            id.Value,
                            ReadInt(el, "return_deadline"),
                            ReadBool(el, "accepts_returns") ?? false,
                            ReadBool(el, "accepts_exchanges") ?? false));
                    }
                }
            }
        }
        catch (JsonException)
        {
            throw new BusinessException("TradeXpress:Etsy:Product:ReturnPoliciesParseFailed");
        }

        return result;
    }

    /// <summary>Dükkân bölümü yanıtını parse eder (public static — birim testli): <c>results[]</c> → (shop_section_id,
    /// title). Kimliksiz/başlıksız öğe elenir. Bozuk JSON gövdesi dostane hatayla DURDURUR (kargo profili deseniyle
    /// aynı).</summary>
    public static IReadOnlyList<EtsyShopSectionSummary> ParseShopSections(string payload)
    {
        var result = new List<EtsyShopSectionSummary>();
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in results.EnumerateArray())
                {
                    var id = ReadLong(el, "shop_section_id");
                    var title = ReadString(el, "title");
                    if (id is > 0 && !string.IsNullOrWhiteSpace(title))
                    {
                        result.Add(new EtsyShopSectionSummary(id.Value, title!.Trim()));
                    }
                }
            }
        }
        catch (JsonException)
        {
            throw new BusinessException("TradeXpress:Etsy:Product:ShopSectionsParseFailed");
        }

        return result;
    }

    /// <summary>Dükkân bölümü create/update TEK-öğe yanıtını parse eder (public static — birim testli): kök gövde
    /// (<c>results[]</c> DEĞİL) → (shop_section_id, title). Kimliksiz/başlıksız yanıt dostane hatayla DURDURUR (sessizce
    /// boş dönmek "yazıldı ama seçilemiyor" ile karışırdı).</summary>
    public static EtsyShopSectionSummary ParseShopSection(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var id = ReadLong(doc.RootElement, "shop_section_id");
            var title = ReadString(doc.RootElement, "title");
            if (id is > 0 && !string.IsNullOrWhiteSpace(title))
            {
                return new EtsyShopSectionSummary(id.Value, title!.Trim());
            }

            throw new BusinessException("TradeXpress:Etsy:Product:ShopSectionCreateFailed");
        }
        catch (JsonException)
        {
            throw new BusinessException("TradeXpress:Etsy:Product:ShopSectionsParseFailed");
        }
    }

    /// <summary>İade politikası create/update TEK-öğe yanıtını parse eder (public static — birim testli): kök gövde →
    /// (return_policy_id, return_deadline, accepts_returns, accepts_exchanges). Kimliksiz yanıt dostane hatayla
    /// DURDURUR.</summary>
    public static EtsyReturnPolicySummary ParseReturnPolicy(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var id = ReadLong(doc.RootElement, "return_policy_id");
            if (id is > 0)
            {
                return new EtsyReturnPolicySummary(
                    id.Value,
                    ReadInt(doc.RootElement, "return_deadline"),
                    ReadBool(doc.RootElement, "accepts_returns") ?? false,
                    ReadBool(doc.RootElement, "accepts_exchanges") ?? false);
            }

            throw new BusinessException("TradeXpress:Etsy:Product:ReturnPolicyCreateFailed");
        }
        catch (JsonException)
        {
            throw new BusinessException("TradeXpress:Etsy:Product:ReturnPoliciesParseFailed");
        }
    }

    /// <summary>Kimlik yanıtını parse eder (public static — birim testli): <c>user_id</c> (+ opsiyonel <c>shop_id</c>).
    /// Kimliksiz gövde → null (token geçersiz sayılır). Bozuk JSON gövdesi dostane hatayla DURDURUR.</summary>
    public static EtsyIdentity? ParseIdentity(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var userId = ReadLong(doc.RootElement, "user_id");
            if (userId is not > 0)
            {
                return null;
            }

            return new EtsyIdentity(userId.Value, ReadLong(doc.RootElement, "shop_id"));
        }
        catch (JsonException)
        {
            throw new BusinessException("TradeXpress:Etsy:Product:IdentityParseFailed");
        }
    }

    private static EtsyRemoteListing ReadListing(JsonElement el)
    {
        var offerings = ReadInventory(el, out var inventoryCurrency);
        var imageUrls = ReadInlineImages(el);
        var (_, listingCurrency) = ReadMoney(el, "price");

        return new EtsyRemoteListing(
            ListingId: ReadLong(el, "listing_id") ?? 0,
            Title: ReadString(el, "title") ?? string.Empty,
            Description: ReadString(el, "description"),
            Tags: ReadStringArray(el, "tags"),
            Materials: ReadStringArray(el, "materials"),
            TaxonomyId: ReadLong(el, "taxonomy_id"),
            WhoMade: MapWhoMade(ReadString(el, "who_made")),
            WhenMade: MapWhenMade(ReadString(el, "when_made")),
            ListingType: MapListingType(ReadString(el, "type")),
            ImageUrls: imageUrls,
            CurrencyCode: listingCurrency ?? inventoryCurrency,
            Offerings: offerings);
    }

    // Listeleme gövdesindeki gömülü inventory (includes=Inventory) → offering listesi. Yoksa boş (fallback tamamlar).
    private static IReadOnlyList<EtsyRemoteOffering> ReadInventory(JsonElement listing, out string? currencyCode)
    {
        currencyCode = null;
        if (listing.ValueKind == JsonValueKind.Object
            && listing.TryGetProperty("inventory", out var inventory)
            && inventory.ValueKind == JsonValueKind.Object)
        {
            return ReadOfferings(inventory, out currencyCode);
        }

        return Array.Empty<EtsyRemoteOffering>();
    }

    // inventory.products[] → offering listesi (her product = bir varyant kombinasyonu).
    private static IReadOnlyList<EtsyRemoteOffering> ReadOfferings(JsonElement inventory, out string? currencyCode)
    {
        currencyCode = null;
        var result = new List<EtsyRemoteOffering>();
        if (inventory.ValueKind != JsonValueKind.Object
            || !inventory.TryGetProperty("products", out var products)
            || products.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var product in products.EnumerateArray())
        {
            var (price, quantity, isEnabled, offeringCurrency) = ReadFirstOffering(product);
            currencyCode ??= offeringCurrency;

            result.Add(new EtsyRemoteOffering(
                Sku: ReadString(product, "sku"),
                Quantity: quantity,
                Price: price,
                IsEnabled: isEnabled,
                EtsyProductId: ReadLong(product, "product_id") ?? 0,
                Properties: ReadPropertyValues(product)));
        }

        return result;
    }

    // product.offerings[0] → (price, quantity, is_enabled, currency). Etsy'de offering-başına tek fiyat/adet.
    private static (decimal? Price, int Quantity, bool IsEnabled, string? Currency) ReadFirstOffering(JsonElement product)
    {
        if (product.ValueKind == JsonValueKind.Object
            && product.TryGetProperty("offerings", out var offerings)
            && offerings.ValueKind == JsonValueKind.Array)
        {
            foreach (var offering in offerings.EnumerateArray())
            {
                var (price, currency) = ReadMoney(offering, "price");
                var quantity = ReadInt(offering, "quantity") ?? 0;
                var isEnabled = ReadBool(offering, "is_enabled") ?? true;
                return (price, quantity, isEnabled, currency);
            }
        }

        return (null, 0, true, null);
    }

    // product.property_values[] → (name, value) çiftleri (varyant ekseni seçimi). value = values[0].
    private static IReadOnlyList<EtsyRemoteProperty> ReadPropertyValues(JsonElement product)
    {
        var result = new List<EtsyRemoteProperty>();
        if (product.ValueKind != JsonValueKind.Object
            || !product.TryGetProperty("property_values", out var propertyValues)
            || propertyValues.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var pv in propertyValues.EnumerateArray())
        {
            var name = ReadString(pv, "property_name");
            var value = ReadFirstArrayString(pv, "values");
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(value))
            {
                result.Add(new EtsyRemoteProperty(name!.Trim(), value!.Trim()));
            }
        }

        return result;
    }

    // Listeleme gövdesindeki gömülü images (includes=Images) → URL listesi. Yoksa boş (fallback tamamlar).
    private static IReadOnlyList<string> ReadInlineImages(JsonElement listing)
    {
        var result = new List<string>();
        if (listing.ValueKind == JsonValueKind.Object
            && listing.TryGetProperty("images", out var images)
            && images.ValueKind == JsonValueKind.Array)
        {
            foreach (var img in images.EnumerateArray())
            {
                var url = ReadImageUrl(img);
                if (!string.IsNullOrWhiteSpace(url))
                {
                    result.Add(url!);
                }
            }
        }

        return result;
    }

    // Etsy görsel öğesi çok boyut döner — en büyük tercih edilir, sonra makul boyutlara düşülür.
    private static string? ReadImageUrl(JsonElement image)
    {
        if (image.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ReadString(image, "url_fullxfull")
            ?? ReadString(image, "url_570xN")
            ?? ReadString(image, "url_170x135");
    }

    // ── Etsy enum eşlemesi (wire string → domain enum; when_made bilinmeyende FAIL-FAST, diğerleri null/varsayılan) ──

    private static EtsyWhoMade? MapWhoMade(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "i_did" => EtsyWhoMade.IDid,
            "someone_else" => EtsyWhoMade.SomeoneElse,
            "collective" => EtsyWhoMade.Collective,
            _ => null,
        };
    }

    /// <summary>Etsy <c>when_made</c> wire string'i → <see cref="ProductMadePeriod"/> (public static — birim testli).
    /// Etsy openapi enum'unun 19 değeriyle BİREBİR (TEK adapter tablosu — Etsy rolling kovayı ör. <c>2020_2027</c>
    /// yaptığında yalnız buradaki satır değişir, enum + DB verisi değişmez). Alan HİÇ gelmediyse (null/boş) null →
    /// import ürün varsayılanını korur. BİLİNMEYEN değer ise sessizce yutulMAZ — fail-fast <see cref="BusinessException"/>
    /// (K9 "gizli kayıp" kapanışı: eski davranış bilinmeyeni null'a düşürüp sessizce MadeToOrder bırakıyordu).</summary>
    public static ProductMadePeriod? MapWhenMade(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "made_to_order" => ProductMadePeriod.MadeToOrder,
            "2020_2026" => ProductMadePeriod.Y2020Plus,
            "2010_2019" => ProductMadePeriod.Y2010To2019,
            "2007_2009" => ProductMadePeriod.Y2007To2009,
            "before_2007" => ProductMadePeriod.Before2007,
            "2000_2006" => ProductMadePeriod.Y2000To2006,
            "1990s" => ProductMadePeriod.Y1990s,
            "1980s" => ProductMadePeriod.Y1980s,
            "1970s" => ProductMadePeriod.Y1970s,
            "1960s" => ProductMadePeriod.Y1960s,
            "1950s" => ProductMadePeriod.Y1950s,
            "1940s" => ProductMadePeriod.Y1940s,
            "1930s" => ProductMadePeriod.Y1930s,
            "1920s" => ProductMadePeriod.Y1920s,
            "1910s" => ProductMadePeriod.Y1910s,
            "1900s" => ProductMadePeriod.Y1900s,
            "1800s" => ProductMadePeriod.Y1800s,
            "1700s" => ProductMadePeriod.Y1700s,
            "before_1700" => ProductMadePeriod.Before1700,
            _ => throw new BusinessException("TradeXpress:Etsy:Product:UnknownWhenMade")
                .WithData("value", value),
        };
    }

    private static EtsyListingType MapListingType(string? value)
    {
        return string.Equals(value?.Trim(), "download", StringComparison.OrdinalIgnoreCase)
            ? EtsyListingType.Download
            : EtsyListingType.Physical;
    }

    // ── Etsy-özel + defansif JSON okuyucular (sipariş istemcisiyle aynı toleranslar) ──────────────────

    /// <summary>Etsy money-object: <c>{amount, divisor, currency_code}</c> → (amount/divisor, currency_code). Alan yoksa
    /// (null, null). divisor 0/yoksa güvenli 1.</summary>
    private static (decimal? Value, string? CurrencyCode) ReadMoney(JsonElement obj, string property)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(property, out var money)
            || money.ValueKind != JsonValueKind.Object)
        {
            return (null, null);
        }

        var amount = ReadLong(money, "amount");
        if (amount is null)
        {
            return (null, ReadString(money, "currency_code"));
        }

        var divisor = ReadLong(money, "divisor");
        var effectiveDivisor = divisor is null or 0 ? 1L : divisor.Value;
        return ((decimal)amount.Value / effectiveDivisor, ReadString(money, "currency_code"));
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement obj, string property)
    {
        var result = new List<string>();
        if (obj.ValueKind == JsonValueKind.Object
            && obj.TryGetProperty(property, out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in arr.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String && el.GetString() is { Length: > 0 } value)
                {
                    result.Add(value);
                }
            }
        }

        return result;
    }

    private static string? ReadFirstArrayString(JsonElement obj, string property)
    {
        if (obj.ValueKind == JsonValueKind.Object
            && obj.TryGetProperty(property, out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in arr.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String && el.GetString() is { Length: > 0 } value)
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static string? ReadString(JsonElement obj, string property)
    {
        if (obj.ValueKind == JsonValueKind.Object
            && obj.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.String)
        {
            var value = el.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }

    private static int? ReadInt(JsonElement obj, string property)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(property, out var el))
        {
            return null;
        }

        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetInt32(out var i) => i,
            JsonValueKind.String when int.TryParse(el.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
            _ => null,
        };
    }

    private static long? ReadLong(JsonElement obj, string property)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(property, out var el))
        {
            return null;
        }

        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetInt64(out var l) => l,
            JsonValueKind.String when long.TryParse(el.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) => l,
            _ => null,
        };
    }

    private static bool? ReadBool(JsonElement obj, string property)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(property, out var el))
        {
            return null;
        }

        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private static string Truncate(string value)
    {
        return value.Length <= 500 ? value : value.Substring(0, 500);
    }
}
