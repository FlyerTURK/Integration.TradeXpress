namespace Integration.Framework.Blazor.Client.Services.Mdi;

/// <summary>
/// Edit formunun <b>yapısal başlığı</b> — tek kaynak, üç tüketici (popup header, MDI tab caption,
/// top-panel aktif-tab başlığı). Edit sayfası (<c>CrudEditComponentBase</c>) model yüklenince kurar
/// ve dirty geçişlerinde tazeler. Düz menü/liste tab'ları bunu kullanmaz (onlar düz <c>Title</c>).
/// 3 satır: L1 = (Yeni) tür adı · L2 = kayıt kimliği (Code) · L3 = parent etiketi : parent değeri.
/// </summary>
public sealed record TabHeaderData
{
    /// <summary>L1 — entity tür adı (ör. "Para Birimi").</summary>
    public required string FormCaption { get; init; }

    /// <summary>Yeni kayıtta L1 öneki (ör. "Yeni"); mevcut kayıtta null.</summary>
    public string? NewPrefix { get; init; }

    /// <summary>L2 — kaydın kimlik değeri (ör. Code "USD"); yeni kayıtta null.</summary>
    public string? EntityValue { get; init; }

    /// <summary>L3a — parent etiketi (ör. "Şirket").</summary>
    public string? ParentLabel { get; init; }

    /// <summary>L3b — parent değeri (ör. "MERKEZ").</summary>
    public string? ParentValue { get; init; }

    /// <summary>İkon CSS sınıfı (ör. "fas fa-coins").</summary>
    public string? IconCssClass { get; init; }

    public bool HasParent => !string.IsNullOrEmpty(ParentLabel) && !string.IsNullOrEmpty(ParentValue);
}
