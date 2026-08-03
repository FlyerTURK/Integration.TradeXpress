namespace Integration.TradeXpress.Inbox;

/// <summary>
/// Ortak gelen kutusu PANOSUNDAKİ bir kaynağın (kart türünün) kimliği. Her <c>IInboxSummaryProvider</c> kendini
/// bu anahtarlardan biriyle tanıtır; pano kartları buna göre ayırt eder, UI ikon/rota eşlemesini buna bağlar.
///
/// <para><b>Neden enum DEĞİL de string sabit?</b> Pano AÇIK UÇLU bir uzantı noktasıdır: yeni bir tür (yarın
/// kullanıcı mesajlaşması, sonra sipariş uyarıları, iş akışı onayları...) yalnız yeni bir SAĞLAYICI yazılarak
/// eklenir. Anahtar enum olsaydı her yeni tür MERKEZİ bir tipi değiştirmeyi zorunlu kılardı — yani "eklemeli"
/// olması gereken bir genişleme "mevcut tipi düzenleme"ye dönerdi (Open/Closed ihlali). Ayrıca enum değerleri
/// sayı olarak serileştiğinden sıralamayı bozmak sessiz veri kayması üretir; string anahtar kendi kendini
/// açıklar ve log/telemetride okunabilir kalır. Sabitler yine de BURADA toplanır: sağlayıcılar ham metin
/// ("Confirmations") yazmasın, tek kaynaktan (SSOT) alsın — yazım hatası derlemede yakalanır.</para>
///
/// <para><b>Kural:</b> anahtar bir kez yayımlandıktan sonra DEĞİŞTİRİLMEZ (UI tercihleri/rota eşlemesi ona
/// bağlanır); yeni tür = yeni sabit.</para>
/// </summary>
public static class InboxSourceKey
{
    /// <summary>Teyitler (organizasyon-içi karşılıklı ayna onayı). Kaynak modül SALT-OKUNUR tüketilir —
    /// pano ondan yalnız özet sayar; teyit ekranı/entity'si panoya TAŞINMAZ.</summary>
    public const string Confirmations = "Confirmations";

    /// <summary>Pazaryeri müşteri soruları (kanal-nötr ortak soru gelen kutusu).</summary>
    public const string ChannelQuestions = "ChannelQuestions";
}
