using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.ChannelQuestions;

/// <summary>
/// Soru senkronunun BELLEK-İÇİ sinyal tahtası: UI'ın "şu kanalı sıradaki turda öncelikli çek" işareti + turlar
/// arası ADALET imleci. Hiçbir metodu pazaryerine GİTMEZ; yalnız worker'ın bir sonraki turda ne seçeceğini etkiler.
///
/// <para><b>Neden kalıcı DEĞİL (bilinçli seçim):</b> öncelik işareti bir KULLANICI NİYETİDİR ve ömrü saniyelerle
/// ölçülür — worker en geç bir dakika içinde turu döner. DB'ye yazmak, sayfayı açan her kullanıcı için bir yazma
/// işlemi (ve kilit) demek olurdu; buna karşılık kazanç yalnızca "uygulama tam o saniyede yeniden başlarsa işaret
/// kaybolmasın" olurdu. İşaret kaybolduğunda da veri kaybı YOKTUR: kanal rutin tazeleme sırasına düşer ve en geç
/// <see cref="ChannelQuestionSyncConsts.RoutineRefreshMinutes"/> dakika içinde zaten çekilir.</para>
///
/// <para><b>Tekil süreç varsayımı:</b> worker YALNIZ Blazor host'ta kayıtlıdır ve Blazor SERVER olduğu için UI
/// çağrıları da aynı süreçte koşar → işaret worker'a ulaşır. <b>Sınır:</b> aynı çağrı HttpApi.Host (:44388)
/// üzerinden gelirse işaret o sürecte kalır ve worker görmez; kanal yine rutin tazelemeyle çekilir (davranış
/// bozulmaz, yalnız "hemen" olmaz). Dağıtık bir kuyruk gerekirse bu sınıf tek değişim noktasıdır.</para>
/// </summary>
public class ChannelQuestionSyncSignals : ISingletonDependency
{
    /// <summary>Öncelik istenen kanallar (küme semantiği — aynı kanal için ikinci istek yeni iş üretmez).</summary>
    private readonly ConcurrentDictionary<Guid, byte> _priorityChannelIds = new();

    /// <summary>Kanal başına SON DENEME anı — tur içinde adalet (round-robin) imleci. DB'ye yazılmaz: kota
    /// sınırına takılan (RateLimited) bir tur HİÇBİR ŞEY yazmamalı, ama bir sonraki turda aynı kanalı yeniden
    /// seçip kilitlenmemeliyiz. Bellekteki bu damga tam olarak o boşluğu kapatır.</summary>
    private readonly ConcurrentDictionary<Guid, DateTime> _lastAttemptUtc = new();

    /// <summary>UI işareti: bu kanal sıradaki turda öncelikli çekilsin.</summary>
    public virtual void RequestPriority(Guid salesChannelId)
    {
        if (salesChannelId == Guid.Empty)
        {
            return;
        }

        _priorityChannelIds[salesChannelId] = 0;
    }

    public virtual bool HasPriority(Guid salesChannelId)
    {
        return _priorityChannelIds.ContainsKey(salesChannelId);
    }

    /// <summary>İşareti TÜKETİR — yalnız çekim BAŞARIYLA yapıldığında çağrılır. Kota hatasında işaret DURUR,
    /// böylece kullanıcının isteği bir sonraki tura devreder (aksi hâlde tek başarısız tur isteği yutardı).</summary>
    public virtual void ClearPriority(Guid salesChannelId)
    {
        _priorityChannelIds.TryRemove(salesChannelId, out _);
    }

    /// <summary>Öncelik bekleyen kanalların anlık kopyası (planlama fazı bunu okur).</summary>
    public virtual IReadOnlyCollection<Guid> GetPriorityChannelIds()
    {
        return _priorityChannelIds.Keys.ToList();
    }

    /// <summary>Bu kanala tur ayrıldığını işaretler (başarılı/başarısız FARK ETMEZ — adalet imleci).</summary>
    public virtual void RegisterAttempt(Guid salesChannelId, DateTime nowUtc)
    {
        _lastAttemptUtc[salesChannelId] = nowUtc;
    }

    /// <summary>Kanalın en son ne zaman tur aldığı; hiç almadıysa <see cref="DateTime.MinValue"/> (en öne geçer).</summary>
    public virtual DateTime GetLastAttemptUtc(Guid salesChannelId)
    {
        return _lastAttemptUtc.TryGetValue(salesChannelId, out var value) ? value : DateTime.MinValue;
    }
}
