namespace Integration.TradeXpress.Products;

/// <summary>Ürün görselinin kaynağı — dış URL ya da yüklenmiş dosya (blob storage).</summary>
public enum ProductImageSourceType : byte
{
    /// <summary>Dış görsel bağlantısı — marketplace push'unda DOĞRUDAN kullanılır.</summary>
    Url = 1,

    /// <summary>Yüklenmiş dosya (ABP blob storage / Database provider). Push'ta dış URL üretimi
    /// production aşamasında geçici dosya-hosting entegrasyonuyla yapılacak (2026-07-07 kullanıcı kararı).</summary>
    Upload = 2,
}
