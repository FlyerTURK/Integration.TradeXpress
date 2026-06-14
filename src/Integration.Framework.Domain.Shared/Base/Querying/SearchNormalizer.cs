using System.Collections.Generic;

namespace Integration.Framework.Base.Querying;

/// <summary>
/// Arama/eşleştirme için metni <b>aksan- ve harf-katlayarak</b> ASCII küçük harfe
/// indirger ("fold"): Türkçe ve yaygın aksanlı harfler İngilizce karşılığına çevrilir.
/// Böylece <c>ÜMRANİYE</c>, <c>ÜMRANıYE</c> ve DB'deki <c>Umraniye</c> hepsi
/// <c>umraniye</c> olur; <c>U</c> yazınca <c>u/ü/û</c> tümü eşleşir.
///
/// <para><b>Neden Replace zinciri + ToLower:</b> Aynı katlama kuralı
/// <see cref="ListQueryableExtensions"/> içinde <c>REPLACE(...)+LOWER(...)</c> olarak
/// EF Core tarafından SQL'e çevrilebilir bir Expression'a dönüştürülür. Hem arama terimi
/// (C# tarafı, burası) hem DB değeri (SQL tarafı) <b>aynı</b> haritayla katlandığı için
/// in-memory test ile üretim SQL davranışı tutarlı olur — ve <c>ToLowerInvariant</c>'ın
/// EF'e çevrilememe sorunu da ortadan kalkar (büyük I/İ/ı önce map'lenir).</para>
/// </summary>
public static class SearchNormalizer
{
    /// <summary>
    /// Katlama eşlemeleri (büyük→küçük + aksanlı→ASCII). Türkçe-I varyantları (I/İ/ı)
    /// burada açıkça 'i'ye map'lendiği için sonraki <c>ToLower</c> aşaması kültürden
    /// (Türkçe noktasız-ı tuzağından) bağımsız ve deterministik olur.
    /// </summary>
    // Bu liste İKİ tarafı da besler: C# terim katlaması (Fold) ve SQL'e çevrilen
    // member ifadesi (ListQueryableExtensions.BuildFoldExpression). Yeni bir dil/karakter
    // eklemek için sadece buraya satır ekle — her iki taraf otomatik senkron kalır.
    // (Generic NFD/ToLowerInvariant kullanılamaz: EF Core onları SQL'e çeviremez, dolayısıyla
    //  member ve term tarafları ayrışırdı.)
    public static readonly IReadOnlyList<(string From, string To)> FoldReplacements = new (string, string)[]
    {
        // Türkçe
        ("ç", "c"), ("Ç", "c"),
        ("ğ", "g"), ("Ğ", "g"),
        ("ı", "i"), ("İ", "i"), ("I", "i"),
        ("ö", "o"), ("Ö", "o"),
        ("ş", "s"), ("Ş", "s"),
        ("ü", "u"), ("Ü", "u"), ("û", "u"), ("Û", "u"),
        ("â", "a"), ("Â", "a"),
        ("î", "i"), ("Î", "i"),
        ("ô", "o"), ("Ô", "o"),
        // İskandinav (Norveççe/Danca/İsveççe)
        ("å", "a"), ("Å", "a"),
        ("ø", "o"), ("Ø", "o"),
        ("æ", "ae"), ("Æ", "ae"),
        // Diğer yaygın Avrupa aksanları
        ("é", "e"), ("É", "e"), ("è", "e"), ("È", "e"), ("ê", "e"), ("Ê", "e"), ("ë", "e"), ("Ë", "e"),
        ("á", "a"), ("Á", "a"), ("à", "a"), ("À", "a"), ("ä", "a"), ("Ä", "a"),
        ("ó", "o"), ("Ó", "o"), ("ò", "o"), ("Ò", "o"),
        ("ú", "u"), ("Ú", "u"), ("ù", "u"), ("Ù", "u"),
        ("í", "i"), ("Í", "i"), ("ì", "i"), ("Ì", "i"), ("ï", "i"), ("Ï", "i"),
        ("ñ", "n"), ("Ñ", "n"),
        ("ß", "ss"),
    };

    /// <summary>Metni ASCII küçük harfe katlar. null/boş → boş string.</summary>
    public static string Fold(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var s = input;
        foreach (var (from, to) in FoldReplacements)
            s = s.Replace(from, to);

        // Kalan ASCII A–Z (I zaten map'lendi) → a–z; kültürden bağımsız.
        return s.ToLowerInvariant();
    }
}
