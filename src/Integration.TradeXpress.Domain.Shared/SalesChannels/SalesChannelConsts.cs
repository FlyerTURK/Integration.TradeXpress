namespace Integration.TradeXpress.SalesChannels;

/// <summary>SalesChannel (Satış Kanalı) alanları için merkezî sınırlar (ProductConsts ile hizalı).</summary>
public static class SalesChannelConsts
{
    public const int CodeMaxLength        = 16;
    public const int NameMaxLength        = 128;
    public const int DescriptionMaxLength = 512;

    /// <summary>Kanal API kimlik bilgisi alanları (AppKey/AppSecret gibi) için ortak üst sınır.</summary>
    public const int ConfigMaxLength      = 256;

    /// <summary>Trendyol "yapıştır" Token'ı = base64(apiKey:apiSecret). İki <see cref="ConfigMaxLength"/> alanı + ':'
    /// ayıracının base64 gösterimini rahat kapsayacak üst sınır (base64 ~%37 şişme). Yalnız giriş alanı — persist edilmez.</summary>
    public const int TokenMaxLength       = 1024;

    /// <summary>Etsy OAuth token kolonları (access "{user_id}.{token}" + rotasyonlu refresh). Etsy token'ları
    /// pratikte ~100 karakter; ileriye dönük rahat pay (opak sır — normalize edilmez, kırpılmaz).</summary>
    public const int OAuthTokenMaxLength  = 1024;
}
