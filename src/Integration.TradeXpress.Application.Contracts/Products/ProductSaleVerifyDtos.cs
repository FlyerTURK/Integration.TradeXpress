using System;
using System.Collections.Generic;

namespace Integration.TradeXpress.Products;

/// <summary>Satışa doğrulama isteği.</summary>
public class ProductSaleVerifyInputDto
{
    public Guid ProductId { get; set; }

    /// <summary>Doğrulanacak varyantlar. <b>Boş/null = ürünün TÜM aktif varyantları</b> — kullanıcı formda
    /// tek tek seçmek zorunda kalmasın (tipik niyet "bu ürünü satışa aç"tır). Dolu geldiğinde yalnız
    /// listelenenler doğrulanır; listede olmayan varyantın durumu DEĞİŞMEZ.</summary>
    public List<Guid> VariantIds { get; set; } = new();
}

/// <summary>Doğrulama sonucu — kaç varyant onaylandı, ürün satışa açıldı mı, neler atlandı.</summary>
public class ProductSaleVerifyResultDto
{
    /// <summary><see cref="ProductSaleStatus.Ready"/>'ye çekilen varyant sayısı.</summary>
    public int VerifiedVariants { get; set; }

    /// <summary>Ürün <see cref="ProductSaleStatus.Ready"/> oldu mu.</summary>
    public bool ProductMarkedReady { get; set; }

    /// <summary>Atlanan varyantların gerekçeleri — SESSİZ geçilmez. Kullanıcı "hepsini doğrula" deyip
    /// bazılarının açılmadığını fark etmezse, ürünün neden hâlâ push edilemediğini asla bulamaz.</summary>
    public List<string> Issues { get; set; } = new();

    /// <summary>Doğrulamayı DURDURMAYAN ama kullanıcının bilmesi gereken uyarılar (ör. KDV eksik, görsel yok,
    /// reçetesiz Fixed varyant) — 2026-08-19 satışa hazırlık paneli ölçeğinin Warning kademesi. Varyant yine <c>Ready</c> olur;
    /// bu liste "doğrulandı ama şunlara bak" der. Issues'tan ayrı tutulur ki UI ikisini karıştırmasın.</summary>
    public List<string> Warnings { get; set; } = new();
}
