namespace Integration.TradeXpress.SalesChannels;

/// <summary>SalesChannel (Satış Kanalı) alanları için merkezî sınırlar (ProductConsts ile hizalı).</summary>
public static class SalesChannelConsts
{
    public const int CodeMaxLength        = 16;
    public const int NameMaxLength        = 128;
    public const int DescriptionMaxLength = 512;

    /// <summary>Kanal API kimlik bilgisi alanları (AppKey/AppSecret gibi) için ortak üst sınır.</summary>
    public const int ConfigMaxLength      = 256;
}
