using System;
using System.Collections.Generic;
using System.Linq;
using Integration.TradeXpress.Products;

namespace Integration.TradeXpress.Blazor.Client.Pages.Products;

/// <summary>
/// SATIŞA-HAZIRLIK ISSUE ENDEKSİ — satışa hazırlık paneli verisinin bileşenlere yayılan hâli (2026-08-19 Hakan kuralı: issue
/// bulunduğu HER seviyede görünsün; her bölüm o bölümün EN YÜKSEK ağırlığıyla renklensin).
///
/// <para><b>Nasıl kullanılır:</b> ürün formu bu endeksi cascade eder (<c>Name="SaleReadinessIndex"</c>); sekme
/// başlığı / grid satırı / iç sekme kendi <see cref="SaleReadinessScope"/> yolunu verip <see cref="MaxSeverity"/>
/// sorar. Hangi bileşenin hangi issue'yu göstereceği burada da kurallaşmaz — yalnız yol ön-eki kıyaslanır.</para>
///
/// <para><b>Değişmez (immutable) ve ucuz:</b> panel her yüklendiğinde yeniden kurulur; sorgular sözlükten okur.
/// Cascade edilen değerin REFERANSI değiştiği için tüketiciler kendiliğinden yeniden çizilir.</para>
/// </summary>
public sealed class SaleReadinessIssueIndex
{
    /// <summary>Issue'su olmayan (ya da paneli henüz yüklenmemiş) form için boş endeks — çağıranlar null kontrolü
    /// yerine bunu kullanır, "issue yok" ile "bilinmiyor" aynı sonucu verir: renk yok.</summary>
    public static readonly SaleReadinessIssueIndex Empty = new(Array.Empty<SaleReadinessIssueDto>());

    private readonly IReadOnlyList<SaleReadinessIssueDto> _issues;

    public SaleReadinessIssueIndex(IReadOnlyList<SaleReadinessIssueDto> issues)
    {
        _issues = issues ?? Array.Empty<SaleReadinessIssueDto>();
    }

    /// <summary>Endeksteki tüm issue'lar (panelin listesi bunu çizer).</summary>
    public IReadOnlyList<SaleReadinessIssueDto> Issues
    {
        get { return _issues; }
    }

    /// <summary>Verilen kapsamın İÇİNDEKİ en yüksek ağırlık; hiç issue yoksa <c>null</c> (renklendirme yapılmaz).
    /// <see cref="SaleReadinessSeverity.Info"/> de döner — çağıran Info'yu renklendirmemeyi seçebilir.</summary>
    public SaleReadinessSeverity? MaxSeverity(string? scope)
    {
        SaleReadinessSeverity? max = null;
        foreach (var issue in _issues)
        {
            if (!SaleReadinessScope.IsWithin(issue.Path, scope))
            {
                continue;
            }

            if (max is null || issue.Severity > max)
            {
                max = issue.Severity;
            }
        }

        return max;
    }

    /// <summary>Kapsam içindeki KARAR GEREKTİREN issue sayısı (Info sayılmaz — bilgi satırı rozet şişirmesin).</summary>
    public int Count(string? scope)
    {
        return _issues.Count(i => i.Severity != SaleReadinessSeverity.Info
                                  && SaleReadinessScope.IsWithin(i.Path, scope));
    }

    /// <summary>Kapsam içindeki BELİRLİ ağırlıktaki issue sayısı — uyarı bandı "N engel · M uyarı" derken
    /// ikisini ayrı ayrı sayar; <see cref="Count"/> ikisini toplar ve bu ayrımı veremez.</summary>
    public int CountOf(SaleReadinessSeverity severity, string? scope)
    {
        return _issues.Count(i => i.Severity == severity && SaleReadinessScope.IsWithin(i.Path, scope));
    }

    /// <summary>Kapsam içindeki issue'lar (en ağırı önde — sunucu sırasını korur).</summary>
    public IReadOnlyList<SaleReadinessIssueDto> For(string? scope)
    {
        return _issues.Where(i => SaleReadinessScope.IsWithin(i.Path, scope)).ToList();
    }

    /// <summary>Kapsamda ENGELLEYİCİ (Error) issue var mı.</summary>
    public bool HasError(string? scope)
    {
        return MaxSeverity(scope) == SaleReadinessSeverity.Error;
    }
}
