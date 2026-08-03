using System;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.SalesChannels;

namespace Integration.TradeXpress.ChannelQuestions;

/// <summary>
/// Bir satış kanalından ürün sorusu ÇEKEN istemci — kanal başına bir uygulama (<c>IN11OrderClient</c> ailesinin
/// soru karşılığı, ama kanal-agnostik tek arayüz altında). Senkron kuyruğu <c>IEnumerable&lt;IChannelQuestionClient&gt;</c>
/// enjekte edip <see cref="ChannelType"/> ile eşleşeni seçer.
///
/// <para><b>Uygulama kaydı:</b> somut sınıf adı arayüzle eşleşmediğinden ABP otomatik expose etmez →
/// <c>[ExposeServices(typeof(IChannelQuestionClient))]</c> + <c>ITransientDependency</c> ile İŞARETLENİR
/// (repodaki <c>IInboxSummaryProvider</c>/<c>IBalanceSheetCategorySource</c> deseni).</para>
///
/// <para><b>ÇAĞIRAN TEK MERKEZDİR (2026-08-01 Hakan kararı).</b> N11 ürün soruları ucu dakikada 1 çağrıya kısıtlı
/// ve kota TÜM hesap için ortaktır; paralellik limiti aşmaz. Bu arayüzü YALNIZ senkron worker'ı çağırır. UI
/// (sayfa açılışı, "Yenile" düğmesi) buraya ASLA inmez — kuyruğa öncelik işareti bırakır, ekranda mevcut veriyi
/// gösterir. Aksi hâlde aynı dakikada sayfayı açan ikinci kullanıcı hem hata alır hem worker'ın kotasını yer.</para>
///
/// <para><b>Sayfa döngüsü BURADA DEĞİL:</b> <see cref="FetchPageAsync"/> TEK sayfa çeker. Döngüyü istemciye
/// koymak, kota duvarına çarpınca çekimin ortasında kalmak demekti; kuyruk her turda bir adım ilerler.</para>
/// </summary>
public interface IChannelQuestionClient
{
    /// <summary>Bu istemcinin hizmet ettiği kanal türü — kuyruk seçim anahtarı.</summary>
    SalesChannelType ChannelType { get; }

    /// <summary>Kanalın soru listesinden TEK sayfa çeker (salt-okuma). Kimlik bilgisi
    /// <paramref name="salesChannelId"/>'den çözülür (çağıran sır taşımaz).
    /// <para><b>Kota duvarı istisna DEĞİLDİR:</b> kanal "dakikada bir listelenebilir" derse
    /// <see cref="RemoteQuestionPage.RateLimited"/> işaretli BOŞ sayfa döner — kuyruk aynı işi bir sonraki tura
    /// erteler. Diğer başarısızlıklar (kimlik hatası, geçersiz aralık, taşıma hatası) dostane
    /// <c>BusinessException</c> fırlatır.</para></summary>
    Task<RemoteQuestionPage> FetchPageAsync(
        Guid salesChannelId, ChannelQuestionQuery query, CancellationToken cancellationToken = default);

    /// <summary>Tek sorunun DETAYINI çeker — müşteri adı/e-postası, soru tarihi ve ham durum yalnız burada gelir.
    /// <para><b>PAHALIDIR:</b> detay çağrısı da aynı dakikalık kotayı yer → yalnız gerçekten gereken satırlar için
    /// çağrılmalıdır (cevaplanmış mı bilgisi listeden zaten okunur).</para>
    /// <para><c>null</c> = detay BU TURDA alınamadı (kayıt yok ya da kota duvarı). Çağıran bunu "soru silinmiş"
    /// diye YORUMLAMAMALI, satırı olduğu gibi bırakıp sonraki turda tekrar denemelidir.</para></summary>
    Task<RemoteQuestion?> FetchDetailAsync(
        Guid salesChannelId, string remoteQuestionId, CancellationToken cancellationToken = default);
}
