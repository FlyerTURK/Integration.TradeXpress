using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Inbox;

/// <summary>
/// ORTAK GELEN KUTUSU (pano) servisi — "neden teyitleri ve mesajlaşmayı tek yerden yönetemiyoruz?" sorusunun
/// karşılığı. Pano bir DAĞITICIDIR: kayıtlı tüm <see cref="IInboxSummaryProvider"/>'ları gezer, her türün
/// kendi ürettiği kartı toplar. Kendisi hiçbir türü tanımaz, hiçbir kaynağın verisini yorumlamaz.
///
/// <para><b>Biçim ÖZET + DERİNLEMESİNE:</b> burada dönen kartlar vitrindir (bekleyen sayısı + son birkaç
/// öğe); işin yapıldığı yer türün KENDİ tam ekranıdır (<see cref="InboxCardDto.TargetUrl"/>). Bu yüzden bu
/// servis aksiyon (onay/cevap/red) BARINDIRMAZ — aksiyonlar kaynak modülün kendi servisinde kalır, yetki ve
/// iş kuralları orada tek yerde enforce edilir.</para>
/// </summary>
public interface IInboxAppService : IApplicationService
{
    /// <summary>Panonun tüm kartlarını <see cref="IInboxSummaryProvider.Order"/> sırasında döndürür.
    /// Kart üretemeyen (izinsiz/kapsamsız) ya da hata veren kaynaklar listeden DÜŞER — pano ayakta kalır.</summary>
    Task<List<InboxCardDto>> GetSummaryAsync();
}
