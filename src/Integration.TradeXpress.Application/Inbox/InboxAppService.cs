using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

namespace Integration.TradeXpress.Inbox;

/// <summary>
/// Ortak gelen kutusu DAĞITICISI (tür-nötr): kayıtlı tüm <see cref="IInboxSummaryProvider"/>'ları gezer,
/// her birinden kartını ister, <see cref="IInboxSummaryProvider.Order"/> sırasına dizip döndürür.
/// <c>ChannelProvisioningAppService</c> ile aynı üsluptadır (çoklu-implementasyon <c>IEnumerable&lt;T&gt;</c>
/// enjeksiyonu + delegasyon); fark: orada TEK sağlayıcı seçilir, burada HEPSİ toplanır.
///
/// <para><b>Buraya tür-özel kod GİRMEZ.</b> Yeni bir kaynak (yarın kullanıcı mesajlaşması) eklemek bu dosyayı
/// değiştirmeyi GEREKTİRMEZ — yeni sağlayıcı yazılır, pano onu kendiliğinden toplar. Bu dosyada bir
/// <c>switch (sourceKey)</c> ya da tür adı görürsen desen kırılmıştır.</para>
///
/// <para><b>İzin:</b> özel izin YOKTUR (yalın <see cref="AuthorizeAttribute"/> = kimliği doğrulanmış olmak
/// yeter). Gerçek yetki kart SEVİYESİNDE, sağlayıcının kendi kaynağının izniyle uygulanır: göremediği tür
/// için sağlayıcı <c>null</c> döner. Panoya ayrı bir izin koymak, kullanıcının zaten göremeyeceği kartlar
/// için ikinci ve kayması kolay bir yol açardı.</para>
/// </summary>
[Authorize]
public class InboxAppService : TradeXpressAppService, IInboxAppService
{
    private readonly IEnumerable<IInboxSummaryProvider> _providers;

    public InboxAppService(IEnumerable<IInboxSummaryProvider> providers)
    {
        _providers = providers;
    }

    public virtual async Task<List<InboxCardDto>> GetSummaryAsync()
    {
        var cards = new List<InboxCardDto>();

        // Sıralama kart ÜRETİMİNDEN önce yapılır: hata veren/kart üretmeyen bir kaynak listeden düşse bile
        // kalan kartların sırası kaymaz. Order eşitse SourceKey ile deterministik kırılır (DI'ın keşif sırası
        // garanti değildir — aynı istek iki farklı sıra döndürmemeli).
        var ordered = _providers
            .OrderBy(p => p.Order)
            .ThenBy(p => p.SourceKey, StringComparer.Ordinal);

        foreach (var provider in ordered)
        {
            var card = await BuildCardSafelyAsync(provider);
            if (card is not null)
            {
                cards.Add(card);
            }
        }

        return cards;
    }

    /// <summary>Tek bir sağlayıcıyı İZOLE yürütür: patlarsa kartı atlar, panoyu ayakta tutar.
    ///
    /// <para><b>Neden yutuluyor:</b> pano N bağımsız kaynağı tek çağrıda toplar; birinin sorgusu (ör. teyit
    /// tarafı) hata verdiğinde kullanıcının diğer TÜM kartlarını kaybetmesi orantısızdır — özet ekranı en
    /// kırılgan kaynağı kadar kırılgan olmamalı. <b>Kök neden GİZLENMİYOR:</b> istisna tam metniyle sunucu
    /// loguna yazılır (boş <c>catch</c> DEĞİL). Aksiyon yolları bu servisten geçmez — orada hatalar normal
    /// şekilde kullanıcıya yükselir.</para></summary>
    private async Task<InboxCardDto?> BuildCardSafelyAsync(IInboxSummaryProvider provider)
    {
        try
        {
            return await provider.BuildCardAsync(InboxConsts.RecentItemCount);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(
                ex,
                "Gelen kutusu kartı üretilemedi (kaynak={SourceKey}); pano bu kart olmadan sunuldu.",
                provider.SourceKey);
            return null;
        }
    }
}
