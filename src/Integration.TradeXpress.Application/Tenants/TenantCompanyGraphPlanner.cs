using System;
using System.Collections.Generic;
using System.Linq;
using Integration.TradeXpress.Companies;

namespace Integration.TradeXpress.Tenants;

/// <summary>Tenant şirket grafının UYGULAMA PLANI — hangi düğüme hangi işlem, hangi SIRAYLA.
///
/// <para>Saf fonksiyon: altyapı gerektirmez, dolayısıyla sıralama kuralları hızlı testlerle sürülebilir.
/// <see cref="TenantAppService"/> planı yalnız YÜRÜTÜR — sıra mantığı serviste tekrar yazılmaz, ikisinin
/// birbirinden sapması yapısal olarak imkânsızdır.</para>
///
/// <para><b>TEK sıralı liste döner</b> (ayrı "upsert"/"silme" listeleri değil). Ayrı listelerde gerçek yürütme
/// sırası servisteki döngülerde kalırdı ve test yalnız bölünmeyi doğrulayabilirdi — "silme en son" garantisi
/// test edilemeyen bir yerde asılı kalırdı.</para></summary>
public static class TenantCompanyGraphPlanner
{
    /// <summary>Grafı sıralı adım listesine çevirir.</summary>
    public static IReadOnlyList<TenantCompanyGraphStep> Plan(IReadOnlyList<CompanyGraphDto> companies)
    {
        var live = companies.Where(c => !c.IsDeleted).ToList();

        // Bozuk girdi savunması: birden çok "merkez" işaretliyse İLKİ kazanır (deterministik).
        // forceOne YOK — hiç merkez işaretlenmemişse rastgele bir şirketi merkez İLAN ETMEYİZ; mevcut merkez
        // DB'de zaten duruyor ve grafın sessizliği "merkezi değiştir" emri değildir.
        var flagged = live.Where(c => c.IsHeadquarters).ToList();
        for (var i = 1; i < flagged.Count; i++)
        {
            flagged[i].IsHeadquarters = false;
        }

        var steps = new List<TenantCompanyGraphStep>();

        // 1) MERKEZ ÖNCE: merkez B'ye devrediliyorsa B önce işlenir; CompanyAppService B'yi merkez yapıp A'yı
        //    DB'de düşürür, sonra A "merkez değil" olarak geldiğinde çakışma kalmaz. Ters sırada A hâlâ
        //    merkezken "merkez değil" gelir ve CannotUnsetHeadquarters ile patlar.
        foreach (var company in live.OrderByDescending(c => c.IsHeadquarters))
        {
            steps.Add(new TenantCompanyGraphStep(
                company.Id == Guid.Empty ? TenantCompanyGraphStepKind.Create : TenantCompanyGraphStepKind.Update,
                company));
        }

        // 2) SİLME EN SON: önce silseydik yeni merkez atanmadan eski merkez düşerdi ve "daima bir merkez kalsın"
        //    guard'ı işlemi ortada keserdi.
        //    Yalnız MEVCUT + silinmiş işaretliler: aynı oturumda açılıp silinen düğüm DB'ye hiç girmedi ve
        //    Guid.Empty ile bir DeleteAsync çağrısı akışı EntityNotFound ile ortada bırakırdı.
        foreach (var company in companies.Where(c => c.IsDeleted && c.Id != Guid.Empty))
        {
            steps.Add(new TenantCompanyGraphStep(TenantCompanyGraphStepKind.Delete, company));
        }

        return steps;
    }
}

/// <summary>Plan adımının türü.</summary>
public enum TenantCompanyGraphStepKind
{
    Create,
    Update,
    Delete,
}

/// <summary>Tek plan adımı. Listede bulunmayan mevcut şirket hiç adım üretmez — DOKUNULMAZ; silme yalnız
/// açık <c>IsDeleted</c> işaretiyle olur.</summary>
public sealed record TenantCompanyGraphStep(TenantCompanyGraphStepKind Kind, CompanyGraphDto Company);
