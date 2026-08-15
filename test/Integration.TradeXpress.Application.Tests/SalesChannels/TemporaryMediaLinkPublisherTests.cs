using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.SalesChannels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.SalesChannels;

/// <summary>
/// GEÇİCİ GÖRSEL LİNKİ YAYINCISI — ağ olmadan (sahte işleyici) gövde/duruş ağı.
///
/// <para><b>Neden ağ:</b> push görselleri artık bu yoldan gidiyor; yanlış form alanı ya da hata metnini URL
/// sanmak, Trendyol'a çekilemeyen adres göndermek demek — HTTP 200 döner, listing görselsiz kalır, log temiz
/// görünür. Kapalı anahtarın dış ağa HİÇ çıkmaması da ayrıca pinli (test/mock ortamları).</para>
/// </summary>
public class TemporaryMediaLinkPublisherTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest;
        public string? LastBody;
        public string ResponseBody = "https://litter.catbox.moe/abc123.jpg";
        public HttpStatusCode StatusCode = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(StatusCode) { Content = new StringContent(ResponseBody) };
        }
    }

    private sealed class FakeFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public FakeFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(_handler);
        }
    }

    private sealed class FakeContentReader : MediaContentReader
    {
        public MediaContentPayload? Payload = new(new byte[] { 1, 2, 3 }, "urun.jpg", "image/jpeg");

        public FakeContentReader()
            : base(null!, null!, null!)
        {
        }

        public override Task<MediaContentPayload?> ReadAsync(Guid mediaId)
        {
            return Task.FromResult(Payload);
        }
    }

    private static IConfiguration Config(bool enabled)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TemporaryMediaLink:Enabled"] = enabled ? "true" : "false",
                ["TemporaryMediaLink:Endpoint"] = "https://ornek.test/api.php",
                ["TemporaryMediaLink:Lifetime"] = "24h",
            })
            .Build();
    }

    private static TemporaryMediaLinkPublisher Build(FakeHandler handler, FakeContentReader? reader = null, bool enabled = true)
    {
        return new TemporaryMediaLinkPublisher(
            new FakeFactory(handler),
            reader ?? new FakeContentReader(),
            Config(enabled),
            NullLogger<TemporaryMediaLinkPublisher>.Instance);
    }

    [Fact]
    public async Task Publishes_multipart_form_and_returns_the_hosted_url()
    {
        var handler = new FakeHandler();
        var url = await Build(handler).PublishAsync(Guid.NewGuid());

        url.ShouldBe("https://litter.catbox.moe/abc123.jpg");
        handler.LastRequest!.RequestUri!.ToString().ShouldBe("https://ornek.test/api.php");
        // Form alanları barındırıcının sözleşmesi — adı yanlış yazmak sessizce boş yükleme üretir.
        handler.LastBody.ShouldContain("name=reqtype");
        handler.LastBody.ShouldContain("fileupload");
        handler.LastBody.ShouldContain("name=time");
        handler.LastBody.ShouldContain("24h");
        handler.LastBody.ShouldContain("name=fileToUpload");
        handler.LastBody.ShouldContain("filename=urun.jpg");
    }

    [Fact]
    public async Task Error_text_response_is_not_mistaken_for_a_url()
    {
        var handler = new FakeHandler { ResponseBody = "File too large" };
        (await Build(handler).PublishAsync(Guid.NewGuid())).ShouldBeNull();
    }

    [Fact]
    public async Task Http_failure_returns_null_instead_of_throwing()
    {
        var handler = new FakeHandler { StatusCode = HttpStatusCode.BadGateway };
        (await Build(handler).PublishAsync(Guid.NewGuid())).ShouldBeNull();
    }

    [Fact]
    public async Task Unreadable_media_short_circuits_without_any_network_call()
    {
        var handler = new FakeHandler();
        var reader = new FakeContentReader { Payload = null };

        (await Build(handler, reader).PublishAsync(Guid.NewGuid())).ShouldBeNull();
        handler.LastRequest.ShouldBeNull();   // içerik yoksa dış ağa HİÇ çıkılmaz
    }

    [Fact]
    public void Disabled_flag_reads_false()
    {
        Build(new FakeHandler(), enabled: false).IsEnabled.ShouldBeFalse();
    }

    /// <summary>KAPI YAPISALDIR: kapalıyken PublishAsync doğrudan çağrılsa bile içerik okunmaz, dış ağa çıkılmaz,
    /// <c>null</c> döner. Eski hâlde bu koruma yalnız çağırandaki <c>IsEnabled</c> koşuluna dayanıyordu — ikinci
    /// bir çağıran eklendiğinde sessizce kaybolurdu (bağımsız denetim bulgusu, 2026-08-14).</summary>
    [Fact]
    public async Task Disabled_publisher_never_touches_the_network_even_when_called_directly()
    {
        var handler = new FakeHandler();
        var reader = new FakeContentReader();

        (await Build(handler, reader, enabled: false).PublishAsync(Guid.NewGuid())).ShouldBeNull();
        handler.LastRequest.ShouldBeNull();
    }
}
