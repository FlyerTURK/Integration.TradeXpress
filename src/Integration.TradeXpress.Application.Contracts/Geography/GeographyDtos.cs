using System;

namespace Integration.TradeXpress.Geography;

/// <summary>İdari alan (il/eyalet) — adres picker'ları için hafif okuma DTO'su (host-global coğrafya referansı).</summary>
public class AdministrativeAreaDto
{
    public Guid Id { get; set; }

    /// <summary>Kaynak kod (N11 il kodu, ISO alt-bölüm kısaltması ya da sembolik "MAIN").</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>ISO 3166-2 alt-bölüm kodu (ör. TR-34, US-AL) — sembolik ana alanda null.</summary>
    public string? Iso3166_2Code { get; set; }

    public override string ToString()
    {
        return Code;
    }
}

/// <summary>Yerellik (ilçe/şehir) — adres picker'ları için hafif okuma DTO'su.</summary>
public class LocalityDto
{
    public Guid Id { get; set; }

    /// <summary>Kaynak kod (N11 ilçe id'si ya da dataset şehir id'si).</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public override string ToString()
    {
        return Code;
    }
}

/// <summary>Mahalle — adres picker'ları için hafif DTO'su. CANLI N11 çekimi (saklanmaz; her seferinde N11'den gelir).
/// Referans DB'de tutulmadığından <see cref="Id"/> N11 mahalle id'sidir (string; Guid değil) ve adres yalnız
/// <see cref="Name"/>'i serbest-metin olarak tutar.</summary>
public class NeighborhoodDto
{
    /// <summary>N11 mahalle id'si (canlı; kalıcı core id değil).</summary>
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public override string ToString()
    {
        return Name;
    }
}
