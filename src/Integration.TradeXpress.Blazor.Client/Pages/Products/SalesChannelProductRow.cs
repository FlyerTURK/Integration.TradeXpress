using System;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.TrendyolProducts;

namespace Integration.TradeXpress.Blazor.Client.Pages.Products;

/// <summary>Birleşik satış-kanalı ürünleri grid'inin İSTEMCİ-TARAFI satır sarmalayıcısı — TEK grid'de N11 + Trendyol
/// kanal ürünlerini birlikte listeler (Kanal kolonu). Her satır bir <see cref="ChannelType"/> + kaynak DTO REFERANSI
/// tutar (<see cref="N11"/> xor <see cref="Trendyol"/> dolu). Düzenleme doğrudan kaynak DTO'yu mutasyona uğratır →
/// ürün grafı (ProductGetDto.SalesChannelProducts / SalesChannelTrendyolProducts) senkron kalır. Görünüm alanları
/// (kod/kategori) tek kaynak DTO'dan türetilir (SSOT). Parametresiz ctor + set'li property'ler: CloneFactory JSON
/// deep-clone (Cancel geri alabilsin) için.</summary>
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

    /// <summary>Grid satır kimliği — kaynak DTO'nun ClientKey'i (graf diff kimliğiyle aynı).</summary>
    public Guid ClientKey
    {
        get { return N11?.ClientKey ?? Trendyol?.ClientKey ?? Guid.Empty; }
    }

    /// <summary>Kaydedilmiş mi (Id dolu) — push/sync yalnız kaydedilmiş satırda.</summary>
    public Guid Id
    {
        get { return N11?.Id ?? Trendyol?.Id ?? Guid.Empty; }
    }

    /// <summary>Kaynak DTO'nun soft-delete işareti (graf save'inde silinecek satır).</summary>
    public bool IsDeleted
    {
        get { return N11?.IsDeleted ?? Trendyol?.IsDeleted ?? false; }
    }

    /// <summary>N11 kanalı mı (aksi halde Trendyol).</summary>
    public bool IsN11
    {
        get { return ChannelType == SalesChannelType.TrN11; }
    }

    /// <summary>"N11" / "Trendyol" — marka etiketi (özel isim; lokalize edilmez). Kanal kolonu görüntüsü.</summary>
    public string ChannelLabel
    {
        get { return IsN11 ? "N11" : "Trendyol"; }
    }

    /// <summary>Kanal-özel kod (N11 SellerCode / Trendyol ProductMainId); boşsa "-".</summary>
    public string DisplayCode
    {
        get
        {
            var code = IsN11 ? N11!.SellerCode : Trendyol!.ProductMainId;
            return string.IsNullOrEmpty(code) ? "-" : code;
        }
    }

    /// <summary>Kanal-özel kategori adı (kaynak DTO'dan).</summary>
    public string CategoryName
    {
        get { return (IsN11 ? N11!.CategoryName : Trendyol!.CategoryName) ?? string.Empty; }
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
    }

    public override string ToString()
    {
        return $"{ChannelLabel}:{DisplayCode}";
    }

    #endregion
}
