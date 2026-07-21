namespace Integration.TradeXpress.Channels;

/// <summary>
/// Bir kanal kurulum ADIMININ sonucu — orkestratör her adımı bu üç durumdan biriyle raporlar (sessiz geçilmez).
/// Adım hataları YUTULUR (throw edilmez) → durum + mesaj olarak biriktirilir; bir adım diğerlerini öldürmez.
/// </summary>
public enum ProvisioningStatus : byte
{
    /// <summary>Adım başarıyla tamamlandı (senkronizasyon yapıldı ya da zaten güncel).</summary>
    Success = 1,

    /// <summary>Ön-koşul karşılanmadığı için adım atlandı (ör. OAuth henüz tamamlanmadı) — hata DEĞİL.</summary>
    Skipped = 2,

    /// <summary>Adım hata verdi (ağ/API arızası) — diğer adımlar yine çalışır; kullanıcı "Yeniden Kur" ile dener.</summary>
    Failed = 3,
}
