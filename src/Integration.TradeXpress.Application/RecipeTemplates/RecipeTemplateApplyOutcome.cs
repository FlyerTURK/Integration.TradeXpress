namespace Integration.TradeXpress.RecipeTemplates;

/// <summary>
/// Şablon uygulamasının SONUCU — kaç varyanta serildiği ve kaç kalemin HÂLÂ iki kez göründüğü.
///
/// <para><b>Neden ikinci sayı var (2026-08-20 inceleme bulgusu):</b> yeniden uygulama, kullanıcının düzenlediği
/// şablon satırını korurken şablonun kendi sürümünü de yeniden kurabiliyordu — o kalem reçetede iki kez
/// görünüyordu (biri kullanıcının, biri şablonun). Bunu SESSİZ bırakmak paketleme/kargo/komisyon kalemini fark
/// edilmeden iki kez fiyatlatırdı. Sayı sıfırdan büyükse kullanıcıya uygulama anında söylenir; hangisinin
/// kalacağına kullanıcı karar verir (yazılım hiçbirini kendiliğinden silmez — "değer değişince kayıtlar kolayca
/// silinmesin" kuralı).</para>
///
/// <para><b>Kapsam daraldı (2026-08-21 çoğalma düzeltmesi):</b> soy kimliği
/// (<c>ProductVariantRecipeLine.SourceTemplateLineId</c>) taşıyan düzenlenmiş satırın şablon karşılığı artık
/// yeniden KURULMAZ — o kalem tek görünür ve bu sayıya GİRMEZ (çoğalmayanı saymak yanlış alarm olurdu). Sayı
/// yalnız kimliksiz (özellik öncesi) düzenlenmiş satırları raporlar; onlarda eski davranış sürer.</para>
/// </summary>
public readonly record struct RecipeTemplateApplyOutcome(int AffectedVariantCount, int PreservedEditedLineCount);
