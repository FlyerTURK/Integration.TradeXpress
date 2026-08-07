using System;
using System.Collections.Generic;
using Integration.TradeXpress.Attachments;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Attachments;

/// <summary>
/// İmzalı medya bağlantısının GÜVENLİK sözleşmesi.
///
/// <para><b>Neden var:</b> bu bağlantı, oturum aramayan tek okuma yüzeyi — pazaryerleri görseli kendi
/// sunucularından çektiği için açıldı. Güvenliği tamamen jetonun doğrulanmasına dayanıyor; buradaki her
/// assert, o dar istisnanın sınırını koruyor. Kurcalanan imza, süresi dolmuş jeton ya da değiştirilmiş
/// tenant kabul edilirse başka tenant'ın medyası okunabilir hale gelir.</para>
/// </summary>
public class MediaPublicLinkProviderTests
{
    private const string Key = "unit-test-signing-key-0123456789";

    [Fact]
    public void Round_trips_media_and_tenant()
    {
        var provider = Build();
        var mediaId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var link = provider.TryCreateLink(mediaId, tenantId).ShouldNotBeNull();
        var token = TokenOf(link);

        var target = provider.TryResolveToken(token).ShouldNotBeNull();
        target.MediaId.ShouldBe(mediaId);
        target.TenantId.ShouldBe(tenantId);
    }

    [Fact]
    public void Host_media_round_trips_without_tenant()
    {
        var provider = Build();
        var mediaId = Guid.NewGuid();

        var token = TokenOf(provider.TryCreateLink(mediaId, null).ShouldNotBeNull());

        var target = provider.TryResolveToken(token).ShouldNotBeNull();
        target.MediaId.ShouldBe(mediaId);
        target.TenantId.ShouldBeNull();
    }

    [Fact]
    public void Link_is_absolute_so_a_remote_marketplace_can_fetch_it()
    {
        // Göreli adres pazaryeri sunucusunda çözülemez — bağlantının tabanı yapılandırmadan gelmeli.
        var link = Build().TryCreateLink(Guid.NewGuid(), Guid.NewGuid()).ShouldNotBeNull();

        link.ShouldStartWith("https://example.test/");
        Uri.IsWellFormedUriString(link, UriKind.Absolute).ShouldBeTrue();
    }

    [Fact]
    public void Tampered_signature_is_rejected()
    {
        var provider = Build();
        var token = TokenOf(provider.TryCreateLink(Guid.NewGuid(), Guid.NewGuid()).ShouldNotBeNull());

        var parts = token.Split('.');

        // İlk karakter GERÇEKTEN değişmeli: sabit "x" yazmak, imza zaten "x" ile başlıyorsa (base64url'de ~1/64)
        // jetonu DEĞİŞTİRMEZDİ ve test kendi kendine kırılırdı — tam süitte nadiren kırmızı, tek başına yeşil.
        var flipped = parts[3].Length > 0 && parts[3][0] == 'x' ? 'y' : 'x';
        parts[3] = parts[3].Length > 0 ? flipped + parts[3][1..] : "x";

        provider.TryResolveToken(string.Join('.', parts)).ShouldBeNull();
    }

    [Fact]
    public void Tampered_tenant_is_rejected()
    {
        // EN KRİTİK vaka: tenant jetonun içinde taşınıyor ve okuma o bağlamda yapılıyor. Kurcalanan tenant
        // kabul edilseydi, geçerli imzalı bir bağlantı başka tenant'ın verisine yönlendirilebilirdi.
        var provider = Build();
        var token = TokenOf(provider.TryCreateLink(Guid.NewGuid(), Guid.NewGuid()).ShouldNotBeNull());

        var parts = token.Split('.');
        parts[1] = Guid.NewGuid().ToString("N");

        provider.TryResolveToken(string.Join('.', parts)).ShouldBeNull();
    }

    [Fact]
    public void Tampered_expiry_is_rejected()
    {
        // Süre imzanın İÇİNDE: uzatmaya çalışmak imzayı bozar. İmza süreden ÖNCE doğrulandığı için
        // süresi uzatılmış jeton hiç değerlendirilmez.
        var provider = Build();
        var token = TokenOf(provider.TryCreateLink(Guid.NewGuid(), Guid.NewGuid()).ShouldNotBeNull());

        var parts = token.Split('.');
        parts[2] = DateTimeOffset.UtcNow.AddYears(5).ToUnixTimeSeconds().ToString();

        provider.TryResolveToken(string.Join('.', parts)).ShouldBeNull();
    }

    [Fact]
    public void Token_signed_with_another_key_is_rejected()
    {
        var token = TokenOf(Build(key: "some-other-key-9876543210").TryCreateLink(Guid.NewGuid(), null).ShouldNotBeNull());

        Build().TryResolveToken(token).ShouldBeNull();
    }

    [Fact]
    public void Malformed_tokens_are_rejected()
    {
        var provider = Build();

        provider.TryResolveToken(null).ShouldBeNull();
        provider.TryResolveToken(string.Empty).ShouldBeNull();
        provider.TryResolveToken("nonsense").ShouldBeNull();
        provider.TryResolveToken("a.b.c").ShouldBeNull();          // parça sayısı eksik
        provider.TryResolveToken("a.b.c.d").ShouldBeNull();        // parçalar ayrıştırılamaz
    }

    [Fact]
    public void No_link_is_issued_without_a_signing_key()
    {
        // Anahtar yoksa bağlantı ÜRETİLMEZ — zayıf/boş imzayla kazara dışarı açılmasın.
        Build(key: null).TryCreateLink(Guid.NewGuid(), Guid.NewGuid()).ShouldBeNull();
    }

    [Fact]
    public void No_link_is_issued_without_a_base_url()
    {
        Build(baseUrl: null).TryCreateLink(Guid.NewGuid(), Guid.NewGuid()).ShouldBeNull();
    }

    [Fact]
    public void Empty_media_id_produces_no_link()
    {
        Build().TryCreateLink(Guid.Empty, Guid.NewGuid()).ShouldBeNull();
    }

    private static string TokenOf(string link)
    {
        return link[(link.LastIndexOf('/') + 1)..];
    }

    private static IMediaPublicLinkProvider Build(
        string? key = Key, string? baseUrl = "https://example.test")
    {
        var settings = new Dictionary<string, string?>
        {
            ["MediaPublicLink:SigningKey"] = key,
            ["MediaPublicLink:BaseUrl"] = baseUrl,
        };

        return new MediaPublicLinkProvider(
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
    }
}
