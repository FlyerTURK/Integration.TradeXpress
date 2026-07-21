using System;
using Integration.TradeXpress.EtsyProducts;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.TrendyolProducts;

namespace Integration.TradeXpress.Blazor.Client.Pages.Products;

/// <summary>Birleşik satış-kanalı ürünleri grid'inin İSTEMCİ-TARAFI satır sarmalayıcısı — TEK grid'de N11 + Trendyol +
/// Etsy kanal ürünlerini birlikte listeler (Kanal kolonu). Her satır bir <see cref="ChannelType"/> + kaynak DTO
/// REFERANSI tutar (<see cref="N11"/> xor <see cref="Trendyol"/> xor <see cref="Etsy"/> dolu). Düzenleme doğrudan
/// kaynak DTO'yu mutasyona uğratır → ürün grafı (ProductGetDto.SalesChannelProducts / SalesChannelTrendyolProducts /
/// SalesChannelEtsyProducts) senkron kalır. Görünüm alanları (kod/kategori) tek kaynak DTO'dan türetilir (SSOT).
/// Parametresiz ctor + set'li property'ler: CloneFactory JSON deep-clone (Cancel geri alabilsin) için.</summary>
public sealed class SalesChannelProductRow
{
    #region Constructors

    /// <summary>JSON deep-clone (CloneFactory) için parametresiz ctor.</summary>
    public SalesChannelProductRow()
    {
    }

    #endregion

    #region Properties

    /// <summary>Satırın kanal türü — hangi kaynak DTO'nun dolu olduğunu + hangi edit formunun açılacağını belirler.</summary>
    public SalesChannelType ChannelType { get; set; }

    /// <summary>N11 kaynak DTO'su (ChannelType TrN11 ise dolu; aksi halde null).</summary>
    public SalesChannelTrN11ProductDto? N11 { get; set; }

    /// <summary>Trendyol kaynak DTO'su (ChannelType TrTrendyol ise dolu; aksi halde null).</summary>
    public SalesChannelTrTrendyolProductDto? Trendyol { get; set; }

    /// <summary>Etsy kaynak DTO'su (ChannelType Etsy ise dolu; aksi halde null).</summary>
    public SalesChannelEtsyProductDto? Etsy { get; set; }

    /// <summary>Grid satır kimliği — kaynak DTO'nun ClientKey'i (graf diff kimliğiyle aynı).</summary>
    public Guid ClientKey
    {
        get { return N11?.ClientKey ?? Trendyol?.ClientKey ?? Etsy?.ClientKey ?? Guid.Empty; }
    }

    /// <summary>Kaydedilmiş mi (Id dolu) — push/sync yalnız kaydedilmiş satırda.</summary>
    public Guid Id
    {
        get { return N11?.Id ?? Trendyol?.Id ?? Etsy?.Id ?? Guid.Empty; }
    }

    /// <summary>Kaynak DTO'nun soft-delete işareti (graf save'inde silinecek satır).</summary>
    public bool IsDeleted
    {
        get { return N11?.IsDeleted ?? Trendyol?.IsDeleted ?? Etsy?.IsDeleted ?? false; }
    }

    /// <summary>N11 kanalı mı.</summary>
    public bool IsN11
    {
        get { return ChannelType == SalesChannelType.TrN11; }
    }

    /// <summary>Etsy kanalı mı.</summary>
    public bool IsEtsy
    {
        get { return ChannelType == SalesChannelType.Etsy; }
    }

    /// <summary>"N11" / "Trendyol" / "Etsy" — marka etiketi (özel isim; lokalize edilmez). Kanal kolonu görüntüsü.</summary>
    public string ChannelLabel
    {
        get
        {
            return ChannelType switch
            {
                SalesChannelType.TrN11 => "N11",
                SalesChannelType.Etsy => "Etsy",
                _ => "Trendyol",
            };
        }
    }

    /// <summary>Kanal-özel kod (N11 SellerCode / Trendyol ProductMainId / Etsy SellerSkuBase); boşsa "-".</summary>
    public string DisplayCode
    {
        get
        {
            var code = ChannelType switch
            {
                SalesChannelType.TrN11 => N11!.SellerCode,
                SalesChannelType.Etsy => Etsy!.SellerSkuBase,
                _ => Trendyol!.ProductMainId,
            };
            return string.IsNullOrEmpty(code) ? "-" : code;
        }
    }

    /// <summary>Kanal-özel kategori adı (N11/Trendyol CategoryName; Etsy okuma-anı çözülmüş taksonomi tam yolu). Etsy'de
    /// ad KALICI saklanmaz → <see cref="SalesChannelEtsyProductDto.TaxonomyName"/> okuma anında synced tablodan çözülür;
    /// id bayatladıysa (reconcile sildi) "#{id} (bayat)", hiç seçilmediyse "-".</summary>
    public string CategoryName
    {
        get
        {
            return ChannelType switch
            {
                SalesChannelType.TrN11 => N11!.CategoryName ?? string.Empty,
                SalesChannelType.Etsy => Etsy!.TaxonomyName ?? (Etsy.TaxonomyId is { } t ? $"#{t} (bayat)" : "-"),
                _ => Trendyol!.CategoryName ?? string.Empty,
            };
        }
    }

    #endregion

    #region Methods

    /// <summary>N11 kaynak DTO'sunu saran satır kurar.</summary>
    public static SalesChannelProductRow ForN11(SalesChannelTrN11ProductDto dto)
    {
        return new SalesChannelProductRow { ChannelType = SalesChannelType.TrN11, N11 = dto };
    }

    /// <summary>Trendyol kaynak DTO'sunu saran satır kurar.</summary>
    public static SalesChannelProductRow ForTrendyol(SalesChannelTrTrendyolProductDto dto)
    {
        return new SalesChannelProductRow { ChannelType = SalesChannelType.TrTrendyol, Trendyol = dto };
    }

    /// <summary>Etsy kaynak DTO'sunu saran satır kurar.</summary>
    public static SalesChannelProductRow ForEtsy(SalesChannelEtsyProductDto dto)
    {
        return new SalesChannelProductRow { ChannelType = SalesChannelType.Etsy, Etsy = dto };
    }

    /// <summary>Soft-delete: kaynak DTO'yu silindi işaretle (graf save'inde gider).</summary>
    public void MarkDeleted()
    {
        if (N11 is { } n11)
        {
            n11.IsDeleted = true;
        }
        else if (Trendyol is { } trendyol)
        {
            trendyol.IsDeleted = true;
        }
        else if (Etsy is { } etsy)
        {
            etsy.IsDeleted = true;
        }
    }

    public override string ToString()
    {
        return $"{ChannelLabel}:{DisplayCode}";
    }

    #endregion
}
