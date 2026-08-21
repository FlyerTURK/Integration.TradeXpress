namespace Integration.Framework;

/// <summary>
/// Kimlik/metin alanlarının merkezî normalize + doğrulama guard'ı. Normalizasyonu
/// <see cref="StringNormalizationExtensions"/>'a delege eder, sonra IsEmpty/Min/Max/Range kontrolü
/// yapıp <b>tipli</b> exception (<see cref="RequiredPropertyException"/> vb.) fırlatır. Entity ve
/// manager aynı guard'ı kullanır → mesaj/kod bilgisi tek yerde (framework). Tüketici tekrar yazmaz.
/// </summary>
public static class StringFieldGuard
{
    /// <summary>Code alanı: normalize (UPPER/_), sonra zorunlu + min + max. Normalize edilmiş değeri döner.</summary>
    public static string NormalizeCode(string? raw, string propertyName, int minLength, int maxLength)
    {
        return ValidateRequiredText(raw.NormalizeAsCode(), propertyName, minLength, maxLength);
    }

    /// <summary>Name alanı: normalize (TitleCase), sonra zorunlu + min + max. Normalize edilmiş değeri döner.</summary>
    public static string NormalizeName(string? raw, string propertyName, int minLength, int maxLength)
    {
        return ValidateRequiredText(raw.NormalizeAsName(), propertyName, minLength, maxLength);
    }

    /// <summary>Kültür-BAĞIMSIZ kod (ISO ülke/para kodu gibi): Trim + ToUpperInvariant (tr-TR 'i'→'İ'
    /// tuzağına düşmez), sonra zorunlu + min + max. Boşluk dönüşümü YAPMAZ (ISO kodunda boşluk olmaz).</summary>
    public static string NormalizeInvariantCode(string? raw, string propertyName, int minLength, int maxLength)
    {
        var normalized = raw?.Trim().ToUpperInvariant() ?? string.Empty;
        return ValidateRequiredText(normalized, propertyName, minLength, maxLength);
    }

    /// <summary>Zorunlu serbest metin (ör. randevu konusu, kur kaynak etiketi): Trim, sonra zorunlu + min + max.
    /// Case/boşluk normalizasyonu YAPMAZ — kullanıcı metnini olduğu gibi korur.</summary>
    public static string EnsureRequiredText(string? value, string propertyName, int minLength, int maxLength)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return ValidateRequiredText(trimmed, propertyName, minLength, maxLength);
    }

    /// <summary>Opsiyonel metin (ör. Description): boş/null serbest; doluysa Trim + min + max. Boşsa <c>null</c> döner.</summary>
    public static string? EnsureOptionalText(string? value, string propertyName, int minLength, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length < minLength)
        {
            throw new TooShortPropertyException(propertyName, minLength);
        }

        if (trimmed.Length > maxLength)
        {
            throw new TooLongPropertyException(propertyName, maxLength);
        }

        return trimmed;
    }

    /// <summary>Sayısal alan (ör. DisplayOrder): [min, max] aralığı zorunlu. Değeri döner.</summary>
    public static int EnsureRange(int value, string propertyName, int min, int max)
    {
        if (value < min || value > max)
        {
            throw new OutOfRangePropertyException(propertyName, min, max);
        }

        return value;
    }

    private static string ValidateRequiredText(string normalized, string propertyName, int minLength, int maxLength)
    {
        if (string.IsNullOrEmpty(normalized))
        {
            throw new RequiredPropertyException(propertyName);
        }

        if (normalized.Length < minLength)
        {
            throw new TooShortPropertyException(propertyName, minLength);
        }

        if (normalized.Length > maxLength)
        {
            throw new TooLongPropertyException(propertyName, maxLength);
        }

        return normalized;
    }
}
