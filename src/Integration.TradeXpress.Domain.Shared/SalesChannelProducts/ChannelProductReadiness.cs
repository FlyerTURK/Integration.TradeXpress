namespace Integration.TradeXpress.SalesChannelProducts;

/// <summary>
/// Kanal ürününün SATIŞA HAZIRLIK kademesi — "bu üründe daha ne yapılacak?" sorusunun tek kelimelik cevabı.
///
/// <para><b>Neden alan, neden hesaplanan bir hücre değil</b> (2026-08-10 Hakan: "satışa hazırlık kolonunu
/// gruplayamıyorum"): hazırlık ekranda <c>HasRecipe</c> + <c>ReadyVariantCount</c>'tan TÜRETİLİYORDU ve
/// arkasında tek bir veri alanı olmadığı için DevExpress kolonu gruplayamıyordu — hücre doluydu ama grid
/// için o kolon YOKTU. Kademeyi gerçek bir alana çevirmek gruplamayı (ve sıralama/filtrelemeyi) açar.</para>
///
/// <para><b>Aynı zamanda TEKRARI kaldırır:</b> listenin varsayılan sıralaması zaten bu üç kovayı elle
/// yeniden hesaplıyordu (<c>HasRecipe ? (ReadyVariantCount == 0 ? 1 : 2) : 0</c>). İki yerde yaşayan aynı
/// kural, biri değişince diğerinin sessizce eskimesi demekti; artık sıralama da bu alanı okur.</para>
///
/// <para><b>Sıra ANLAMLIDIR</b> — sayısal artış "işi biten"e doğru gider, yani varsayılan sıralama bu
/// alanı olduğu gibi kullanır: karar bekleyen üstte, hazır olan altta.</para>
/// </summary>
public enum ChannelProductReadiness
{
    /// <summary>Reçete YOK — ürün henüz sınıflandırılmadı; hesaplanacak bir maliyet/stok de yok.</summary>
    NoRecipe = 0,

    /// <summary>Reçete var ama satılabilir varyant YOK — <c>VerifiedRecipeStamp</c> eksik ya da bayat.</summary>
    NotReady = 1,

    /// <summary>En az bir varyant BUGÜN satılabilir.</summary>
    Ready = 2,
}
