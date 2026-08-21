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
/// <para><b>ŞABLON SOYU İKİ DEĞERDİR</b> (2026-08-20): <see cref="Template"/> = şablonun malı (tazelenir),
/// <see cref="TemplateEdited"/> = şablondan gelmiş ama kullanıcı düzenlemiş (tazelenmez, KORUNUR). Sahiplenmeyi
/// <see cref="Manual"/>'a yazmak YETMEZ — "şablondan geldi mi" bilgisi kaybolur ve satırı ŞABLON SATIRI OLDUĞU
/// İÇİN koruyan diğer yollar (muadil denemesini reçeteye uygulama, muadil önizlemesi, materyalizasyonun
/// "bu varyantta zaten şablon satırı var" nöbetçisi) onu tanıyamaz: biri satırı siler, biri ekranda düşürür,
/// biri de üstüne İKİNCİ bir şablon seti serer. Kaynağı korumak = soyu korumak.</para>
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

    /// <summary>Reçete şablonundan uygulandı ve HÂLÂ DOKUNULMAMIŞ — şablon yeniden uygulandığında bu kaynaklı
    /// satırlar düşürülüp tazelenir (idempotentlik buradan gelir).
    /// <para>Kullanıcı satırı düzenlerse satır <see cref="TemplateEdited"/>'e GEÇER (sahiplenme;
    /// <c>ProductRecipeLineWriter</c> kayıt-öncesi/sonrası değer kıyasıyla karar verir) ve tazeleme sorgusuna
    /// ARTIK GİRMEZ — düzenleme korunur. Doküman 2026-08-20'de gerçeğe uyduruldu: "korunur" YAZIYORDU ama kural
    /// kodda yoktu, düzenlenmiş satır ikinci uygulamada siliniyordu.</para></summary>
    Template = 2,

    /// <summary>Şablondan geldi, KULLANICI DÜZENLEDİ — satır artık onundur: hiçbir otomatik mekanizma değerini
    /// ezmez ve şablon yeniden uygulandığında silinmez.
    /// <para><b>Neden <see cref="Manual"/> değil (2026-08-20 inceleme bulgusu):</b> satırın şablon SOYU,
    /// "kullanıcının" olmasından bağımsız bir bilgidir ve üç ayrı yol ona bakar — muadil denemesini reçeteye
    /// uygulama şablon-soylu OLMAYAN satırları süpürür, muadil önizlemesi şablon-soylu satırları listede tutar,
    /// materyalizasyon "bu varyantta zaten şablon satırı var mı" diye sorar. Soy silinseydi düzenlenen satır
    /// sırasıyla: kalıcı SİLİNİR · önizlemede kaybolup hayalet satıra döner · üstüne ikinci bir şablon seti
    /// serilerek paketleme/kargo/komisyon İKİ KEZ fiyatlanırdı.</para></summary>
    TemplateEdited = 3,
}
