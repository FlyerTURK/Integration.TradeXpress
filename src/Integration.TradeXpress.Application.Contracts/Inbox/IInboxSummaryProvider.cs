using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Inbox;

/// <summary>
/// Ortak gelen kutusu panosunun UZANTI NOKTASI: her tür KENDİ kartını üretir. Pano sayfası ve
/// <c>IInboxAppService</c> hiçbir somut türü tanımaz — yeni bir tür (yarın kullanıcı mesajlaşması) eklemek
/// yalnız yeni bir implementasyon yazmaktır; pano, DTO ve servis DEĞİŞMEZ (Open/Closed).
///
/// <para><b>Kaynağa saygı — pano TÜKETİCİDİR, sahip değil:</b> sağlayıcı kendi kaynağının okuma sorgusunu
/// yazar; kaynak modülün entity'sini/ekranını/servisini panonun ihtiyacı için DEĞİŞTİRMEZ. (Teyitler bunun
/// somut örneğidir: mevcut Teyit ekranı ve entity'si taşınmaz/değiştirilmez, panoya yalnız ÖZET okunur.)</para>
///
/// <para><b>İzin/kapsam kontrolü SAĞLAYICIDADIR:</b> kullanıcının o türü görme izni yoksa ya da kart için
/// gerekli kapsam (working company/branch) yoksa <see cref="BuildCardAsync"/> <c>null</c> döner → kart hiç
/// gösterilmez. İzinsizlikte istisna FIRLATMA: pano tüm türleri tek çağrıda toplar, istisna gürültüsü yerine
/// sessiz gizleme doğru davranıştır (dispatcher yine de savunma amaçlı yakalar ama bu yol yedektir).</para>
///
/// <para><b>Emsal:</b> <c>IChannelProvisioner</c> ve <c>IBalanceSheetCategorySource</c> — ikisi de
/// <c>IEnumerable&lt;T&gt;</c> ile toplanan çoklu-implementasyon deseni; aynı üslup burada da geçerlidir.</para>
///
/// <para><b>DI kaydı (implementasyon yazan için ZORUNLU okuma):</b> bu arayüz <see cref="ITransientDependency"/>
/// türettiği için ABP somut sağlayıcıyı otomatik TRANSIENT kaydeder. ANCAK ABP'nin varsayılan servis TEŞHİRİ
/// yalnız sınıf adı arayüz adıyla BİTİYORSA arayüzü servis olarak açar. Bu yüzden her implementasyon
/// <c>[ExposeServices(typeof(IInboxSummaryProvider))]</c> ile İŞARETLENİR (repodaki <c>*CategorySource</c>
/// deseninin aynısı) — böylece kayıt sınıf ADINDAN bağımsız olur. İşaretlenmezse sağlayıcı sessizce
/// <c>IEnumerable&lt;IInboxSummaryProvider&gt;</c>'a düşmez ve kart hiç görünmez (teşhisi zor bir sessiz hata).</para>
/// </summary>
public interface IInboxSummaryProvider : ITransientDependency
{
    /// <summary>Bu sağlayıcının ürettiği kartın kaynak kimliği — <see cref="InboxSourceKey"/> sabitlerinden biri.</summary>
    string SourceKey { get; }

    /// <summary>Panodaki kart sırası (küçük = önce). Sıralamayı sağlayıcı KENDİ beyan eder; pano sabit bir
    /// tür listesi tutmaz.</summary>
    int Order { get; }

    /// <summary>Kartı üretir. <paramref name="recentCount"/> vitrinde gösterilecek son öğe adedidir
    /// (<see cref="InboxConsts.RecentItemCount"/>) — sağlayıcı sorgusunu BU sayıya göre sınırlar, fazlasını
    /// çekip elde kırpmaz. <c>null</c> dönüş = kart gösterilmez (izin yok / kapsam yok).</summary>
    Task<InboxCardDto?> BuildCardAsync(int recentCount);
}
