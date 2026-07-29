namespace Integration.TradeXpress.Products;

/// <summary>
/// Reçete satırının KAYNAĞI — satırı kimin ürettiği. Otomatik üretilen satırları yeniden üretirken kullanıcının
/// elle girdiklerini korumanın anahtarıdır.
///
/// <para><b>Neden eklendi (2026-07-27):</b> muadil varyant materyalizasyonu, kombinasyon değiştiğinde varyantın
/// TÜM reçete satırlarını siliyor ve yalnız metal satırlarını geri kuruyordu. Kullanıcının elle eklediği hizmet
/// satırı (işçilik, paketleme, kargo…) her yeniden hesaplamada SESSİZCE kayboluyordu; reçete şablonu geldiğinde
/// şablon satırları da aynı akıbete uğrardı. Artık silme yalnız AYNI KAYNAKTAN gelen satırları kapsar.</para>
///
/// <para><see cref="SideCostKind"/> ile karıştırma: o, KANAL gider ayarlarından üretilen satırın TÜRÜNÜ söyler
/// (paketleme/kargo/komisyon…) ve kendi idempotent reconcile'ı vardır. Bu alan ise satırın hangi MEKANİZMA
/// tarafından yazıldığını söyler; ikisi birlikte dolu olabilir.</para>
/// </summary>
public enum RecipeLineOrigin : byte
{
    /// <summary>Kullanıcı satırı (varsayılan) — hiçbir otomatik mekanizma DOKUNMAZ, yalnız kullanıcı siler.</summary>
    Manual = 0,

    /// <summary>Muadillik hesabından üretildi — kombinasyon değişince bu kaynaklı satırlar yenilenir.</summary>
    Substitution = 1,

    /// <summary>Reçete şablonundan uygulandı — şablon yeniden uygulandığında bu kaynaklı satırlar tazelenir.
    /// Kullanıcı uygulandıktan sonra satırı düzenlerse düzenlemesi korunur (yeniden uygulama açık istektir).</summary>
    Template = 2,
}
