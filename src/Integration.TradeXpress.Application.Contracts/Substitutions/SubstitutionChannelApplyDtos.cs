using System;
using System.Collections.Generic;

namespace Integration.TradeXpress.Substitutions;

/// <summary>
/// Muadil M4 <c>ApplySubstitutionAsync</c> girdisi — bir KANAL ürününe Top-N kombinasyonu varyant (StockItem) olarak uygular.
/// Hesap TEK motor zincirinden yeniden koşulur (<see cref="ISubstitutionCalculationAppService"/>);
/// tolerans DAİMA grup ayarından. N11 ve Trendyol adaptörleri AYNI girdiyi kullanır.
/// </summary>
public class SubstitutionApplyInput
{
    public Guid SubstitutionGroupId { get; set; }

    /// <summary>İstenilen miktar (gram) — pozitif olmalı (fail-fast).</summary>
    public decimal TargetQuantity { get; set; }

    /// <summary>Oluşturulacak varyant (kombinasyon) sayısı — kullanıcı seçer (konsept karar 5); pozitif zorunlu.</summary>
    public int TopN { get; set; }

    /// <summary>Stok kapsamı: şube (null = şirketin tümü) — hesapla birebir.</summary>
    public Guid? BranchId { get; set; }

    /// <summary>Stok kapsamı: kasa (null = şubenin tümü) — hesapla birebir.</summary>
    public Guid? VaultId { get; set; }
}

/// <summary>
/// <c>ApplySubstitutionAsync</c> sonucu — uygulanan kombinasyon özetleri + ticari tolerans bildirimi
/// (push açıklamasına iliştirilecek metin; push entegrasyonu ayrı dilim — burada yalnız üretim).
/// </summary>
public class SubstitutionApplyResultDto
{
    /// <summary>Ticari tolerans bildirimi (grup toleransı &gt; 0 ise; ör. "+/− binde 1 tolerans hakkı saklıdır") — yoksa null.</summary>
    public string? ToleranceNotice { get; set; }

    /// <summary>Kanal ürünündeki "Kombinasyon" özelliğinin id'si (<c>ApplySubstitutionAsync</c>'in yönettiği tek özellik).</summary>
    public Guid CombinationAttributeId { get; set; }

    /// <summary>Uygulanan kombinasyonlar, Rank artan sırayla (ilki = ana varyant).</summary>
    public List<SubstitutionAppliedCombinationDto> Items { get; set; } = new();
}

/// <summary>Uygulanan tek kombinasyonun özeti.</summary>
public class SubstitutionAppliedCombinationDto
{
    /// <summary>Skor sırası (1 = ana varyant).</summary>
    public int Rank { get; set; }

    /// <summary>true = ana varyant (en iyi skor; kanal deseninde ilk sıra/DisplayOrder 0).</summary>
    public bool IsPrimary { get; set; }

    /// <summary>Kombinasyon değer metni (ör. "1×10gr + 2×1gr") — kanal özelliğinin görünen değeri.</summary>
    public string ValueText { get; set; } = string.Empty;

    /// <summary>Paket sayısı → StockItem OverrideStock olarak yazıldı.</summary>
    public int PackageCount { get; set; }

    /// <summary>Bu kombinasyon değerini taşıyan StockItem sayısı (kullanıcının diğer özellik eksenleriyle kartezyen).</summary>
    public int StockItemCount { get; set; }
}
