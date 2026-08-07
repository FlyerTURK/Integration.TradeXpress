namespace Integration.TradeXpress.SpecialCodes;

/// <summary>Özel kod (SpecialCode — herhangi bir entity property'sini gruplayan hiyerarşik kod sözlüğü) alan
/// sınırları.</summary>
public static class SpecialCodeConsts
{
    /// <summary>Özel kodda ALT SINIR 2'dir (genel kural 3) — 2026-08-06 Hakan tespiti.
    /// <para>Ölçü birimi kodları doğal olarak iki harflidir: <c>AD</c> (Adet), <c>KG</c>, <c>GR</c>, <c>MT</c>.
    /// Genel 3-harf kuralı burada meşru kodu reddediyordu. Konvansiyonun kendi gerekçesi zaten
    /// <i>"en kısa meşru ada uymalı"</i> diyor; bu sözlükte en kısa meşru ad iki harflidir.</para></summary>
    public const int CodeMinLength         = 2;

    public const int CodeMaxLength         = 32;
    public const int NameMaxLength         = 128;
    public const int EntityNameMaxLength   = 128;   // teknik: hedef entity tipi adı (ör. "Good")
    public const int PropertyNameMaxLength = 128;   // teknik: hedef property adı (ör. "Category")
    public const int DescriptionMaxLength  = 512;
}
