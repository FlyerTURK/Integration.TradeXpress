using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.SalesChannels;

namespace Integration.TradeXpress.ChannelQuestions;

/// <summary>
/// <see cref="IChannelQuestionClient"/>'ın TEST sahtesi (N11 rolünde) — ağ yok, kimlik yok, kota beklemesi yok
/// (<c>FakeN11ProductClient</c> emsali). Senkron kuyruğunun testleri bunun üzerinden yürür: sayfalar
/// SCRIPTLENİR, kota duvarı SİMÜLE edilir ve her çağrı SAYILIR.
///
/// <para><b>Çağrı sayacı neden birinci sınıf:</b> bu ailenin en pahalı hatası "tek merkez" kuralının sessizce
/// delinmesidir — N11 dakikada 1 çağrıya izin verir, ikinci bir tetikleyici eklenirse worker kotasız kalır ve
/// hata canlıda görülür. Sayaç, kuyruğun bir turda KAÇ kez kanala gittiğini teste sabitler.</para>
///
/// <para>DI'ya KAYDEDİLMEZ: gerçek <c>N11QuestionClient</c> ile aynı arayüzü sunar, ikisi birden kayıtlı olsaydı
/// <c>IEnumerable&lt;IChannelQuestionClient&gt;</c> çözümü belirsizleşirdi. Testler doğrudan örnekler.</para>
/// </summary>
public sealed class FakeN11QuestionClient : IChannelQuestionClient
{
    private readonly Queue<RemoteQuestionPage> _scriptedPages = new();
    private readonly Dictionary<string, RemoteQuestion> _scriptedDetails = new(StringComparer.Ordinal);

    /// <summary>Açıkken HER çağrı kota duvarına takılmış gibi davranır (liste → <c>RateLimited</c> sayfa,
    /// detay → <c>null</c>). Scriptlenmiş sayfa TÜKETİLMEZ: gerçekte de kota hatası veri getirmez.</summary>
    public bool SimulateRateLimit { get; set; }

    /// <summary>Bu istemciye ulaşan liste istekleri (sırayla) — kuyruğun hangi sayfayı/aralığı istediğini doğrular.</summary>
    public List<ChannelQuestionQuery> ListRequests { get; } = new();

    /// <summary>Detayı istenen soru kimlikleri (sırayla) — "detay yalnız gereken satır için" kuralının kanıtı.</summary>
    public List<string> DetailRequests { get; } = new();

    /// <summary>Kanala giden TOPLAM çağrı (liste + detay) — dakikada-1 kotasını paylaşan her istek buraya sayılır.</summary>
    public int TotalCallCount
    {
        get
        {
            return ListRequests.Count + DetailRequests.Count;
        }
    }

    public SalesChannelType ChannelType
    {
        get
        {
            return SalesChannelType.TrN11;
        }
    }

    /// <summary>Sıradaki çağrının döndüreceği sayfayı kuyruğa ekler (çağrı sırasıyla tüketilir).</summary>
    public void ScriptPage(RemoteQuestionPage page)
    {
        _scriptedPages.Enqueue(page);
    }

    /// <summary>Kolay kurulum: verilen soruları TEK sayfa olarak scriptler.</summary>
    public void ScriptPage(params RemoteQuestion[] questions)
    {
        _scriptedPages.Enqueue(new RemoteQuestionPage(questions, questions.Length, PageCount: 1, RateLimited: false));
    }

    /// <summary>Bir sorunun detay yanıtını scriptler; scriptlenmemiş kimlik <c>null</c> döner.</summary>
    public void ScriptDetail(RemoteQuestion detail)
    {
        _scriptedDetails[detail.RemoteQuestionId] = detail;
    }

    public Task<RemoteQuestionPage> FetchPageAsync(
        Guid salesChannelId, ChannelQuestionQuery query, CancellationToken cancellationToken = default)
    {
        ListRequests.Add(query);

        if (SimulateRateLimit)
        {
            return Task.FromResult(RemoteQuestionPage.FromRateLimit());
        }

        // Script bittiğinde BOŞ sayfa (kota hatası DEĞİL): "kanalda başka kayıt yok" hâlinin karşılığı.
        var page = _scriptedPages.Count > 0
            ? _scriptedPages.Dequeue()
            : new RemoteQuestionPage(Array.Empty<RemoteQuestion>(), TotalCount: 0, PageCount: 0, RateLimited: false);
        return Task.FromResult(page);
    }

    public Task<RemoteQuestion?> FetchDetailAsync(
        Guid salesChannelId, string remoteQuestionId, CancellationToken cancellationToken = default)
    {
        DetailRequests.Add(remoteQuestionId);

        if (SimulateRateLimit)
        {
            return Task.FromResult<RemoteQuestion?>(null);
        }

        return Task.FromResult<RemoteQuestion?>(
            _scriptedDetails.TryGetValue(remoteQuestionId, out var detail) ? detail : null);
    }
}
