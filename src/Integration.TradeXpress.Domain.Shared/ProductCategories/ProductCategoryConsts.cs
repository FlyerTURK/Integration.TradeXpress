namespace Integration.TradeXpress.ProductCategories;

/// <summary>
/// Ürün kategorisi (core taraf — pazaryeri kategorisi değil; şirkete ait) alan sınırları. Nitelik adı/değer uzunlukları agnostik nitelik
/// sistemiyle (<c>EntityVariantConsts</c>) HİZALI: kategori nitelikleri ürünün nitelik grafına yansıyacağı
/// için aynı sınırlara tabi olmalı — daha geniş bir sınır, yansıma anında sessiz kırpılmaya yol açardı.
///
/// <para><b>KOD ALANI YOK</b> (2026-07-27 Hakan kararı): kategori bir taksonomi düğümüdür, kimliği AD + AĞAÇTAKİ
/// YERİDİR. Kod istemek her kayıtta gereksiz sürtünme yaratırdı; kanal eşleştirmesi de koda değil kalıcı
/// <c>Id</c>'ye asılır. Benzersizlik bunun yerine KARDEŞ düzeyinde: aynı üst altında aynı ad iki kez olamaz
/// (böylece "Takı › Yüzük" ile "Saat › Yüzük" ikisi de meşru kalır).</para>
/// </summary>
public static class ProductCategoryConsts
{
    /// <summary>Kategori ADINDA yasak karakterler — yol ayraçları.
    ///
    /// <para>Kategori yolu düz metin olarak kuruluyor (› ile birleştirilir); ada ayraç girerse tek kategori
    /// iki seviye gibi görünür ve yol geri ayrıştırılamaz. Gerçek ayracın yanında gözle ondan ayırt edilemeyen
    /// ASCII &gt; de engellenir — kullanıcı hangisini yazdığını bilmek zorunda kalmasın (2026-08-04 Hakan).</para></summary>
    public static readonly char[] ForbiddenNameCharacters = { '›', '>' };
    public const int NameMaxLength = 128;
    public const int DescriptionMaxLength = 512;

    // DERİNLİK TAVANI YOK (2026-07-27 Hakan kararı): hiyerarşi serbest — gerekirse 20 seviye. Sonsuz yürüyüş
    // riski tavanla değil DÖNGÜ tespitiyle kesilir (ProductCategoryTreeManager, ziyaret edilen düğüm kümesi);
    // düğüm sayısı sonlu olduğundan bu kesin sonlanır. Keyfi bir tavan meşru derin taksonomileri engellerdi.
}
