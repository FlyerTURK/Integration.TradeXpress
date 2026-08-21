namespace Integration.TradeXpress.Products;

/// <summary>
/// Satışa hazırlık panelindeki kontrol listesinin bir adımının durumu (2026-08-19). Sıra anlamlıdır: sayısal artış "iyiye" gider,
/// UI sıralama/özet için olduğu gibi kullanabilir.
/// </summary>
public enum SaleReadinessStepState : byte
{
    /// <summary>Adım ENGELLİ — bu adım geçilmeden ürün satışa çıkamaz (en az bir Error issue'u).</summary>
    Blocked = 0,

    /// <summary>Adım yapılmamış ama engel de değil (ör. henüz kanal ürünü yok).</summary>
    NotStarted = 1,

    /// <summary>Adım geçildi ama dikkat isteyen issue var (Warning).</summary>
    Attention = 2,

    /// <summary>Adım tamam.</summary>
    Done = 3,
}
