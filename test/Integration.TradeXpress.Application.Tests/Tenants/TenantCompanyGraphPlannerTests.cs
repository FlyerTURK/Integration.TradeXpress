using System;
using System.Collections.Generic;
using System.Linq;
using Integration.TradeXpress.Companies;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Tenants;

/// <summary>
/// Tenant şirket grafı DIFF SIRASI — <see cref="TenantCompanyGraphPlanner"/>.
///
/// <para>Bu grafı yazan yol, tenant güncelleme formunun tek kaydetme düğmesidir; sırası bozulursa
/// <c>OrgTreeManager</c> guard'ları işlemi ORTADA keser ve geriye yarım bir org ağacı kalır. Kurallar burada
/// altyapısız sürülür — servis planı yalnız yürütür, kendi sırasını KURMAZ.</para>
/// </summary>
public class TenantCompanyGraphPlannerTests
{
    /// <summary>MERKEZ ÖNCE. Merkez A'dan B'ye devrediliyor: B önce işlenmeli ki <c>CompanyAppService</c>
    /// B'yi merkez yapıp A'yı DB'de düşürsün. Ters sırada A hâlâ merkezken "merkez değil" gelir ve
    /// <c>CannotUnsetHeadquarters</c> ile patlardı.</summary>
    [Fact]
    public void Headquarters_is_written_first_so_the_transfer_does_not_collide()
    {
        var a = Node("A", isHeadquarters: false);
        var b = Node("B", isHeadquarters: true);

        var plan = TenantCompanyGraphPlanner.Plan(new List<CompanyGraphDto> { a, b });

        plan.Select(s => s.Company.Code).ShouldBe(new[] { "B", "A" });
    }

    /// <summary>SİLME EN SON — merkez devri tamamlanmadan eski merkez düşerse "daima bir merkez kalsın"
    /// guard'ı işlemi yarıda keser. Sıra plan listesinin KENDİSİNDE, servis yalnız yürütüyor.</summary>
    [Fact]
    public void Deletions_are_the_last_steps_of_the_plan()
    {
        var keep = Node("KEEP", isHeadquarters: true);
        var edit = Node("EDIT", isHeadquarters: false);
        var drop = Node("DROP", isHeadquarters: false);
        drop.IsDeleted = true;

        var plan = TenantCompanyGraphPlanner.Plan(new List<CompanyGraphDto> { drop, edit, keep });

        plan.Select(s => s.Kind).ShouldBe(new[]
        {
            TenantCompanyGraphStepKind.Update,   // KEEP (merkez → önce)
            TenantCompanyGraphStepKind.Update,   // EDIT
            TenantCompanyGraphStepKind.Delete,   // DROP (en son)
        });
        plan[0].Company.Code.ShouldBe("KEEP");
        plan[^1].Company.Code.ShouldBe("DROP");
    }

    /// <summary>Id'siz düğüm CREATE, id'li düğüm UPDATE adımı üretir.</summary>
    [Fact]
    public void New_node_becomes_create_and_existing_node_becomes_update()
    {
        var existing = Node("OLD", isHeadquarters: false);
        var fresh = Node("NEW", isHeadquarters: false);
        fresh.Id = Guid.Empty;

        var plan = TenantCompanyGraphPlanner.Plan(new List<CompanyGraphDto> { existing, fresh });

        plan.Single(s => s.Company.Code == "OLD").Kind.ShouldBe(TenantCompanyGraphStepKind.Update);
        plan.Single(s => s.Company.Code == "NEW").Kind.ShouldBe(TenantCompanyGraphStepKind.Create);
    }

    /// <summary>Aynı oturumda AÇILIP silinen düğüm DB'ye hiç girmedi → hiç adım üretmez.
    /// <para>Silme adımına girseydi <c>Guid.Empty</c> ile bir <c>DeleteAsync</c> çağrılır ve akış
    /// <c>EntityNotFound</c> ile ortada kalırdı.</para></summary>
    [Fact]
    public void Node_created_and_removed_in_the_same_session_is_ignored_entirely()
    {
        var ghost = Node("GHOST", isHeadquarters: false);
        ghost.Id = Guid.Empty;
        ghost.IsDeleted = true;

        TenantCompanyGraphPlanner.Plan(new List<CompanyGraphDto> { ghost }).ShouldBeEmpty();
    }

    /// <summary>Birden çok "merkez" işaretli gelirse İLKİ kazanır — deterministik, sessiz çelişki kalmaz.</summary>
    [Fact]
    public void Multiple_headquarters_flags_collapse_to_the_first()
    {
        var a = Node("A", isHeadquarters: true);
        var b = Node("B", isHeadquarters: true);

        var plan = TenantCompanyGraphPlanner.Plan(new List<CompanyGraphDto> { a, b });

        plan.Count(s => s.Company.IsHeadquarters).ShouldBe(1);
        a.IsHeadquarters.ShouldBeTrue();
        b.IsHeadquarters.ShouldBeFalse();
    }

    /// <summary>Hiç merkez işaretli değilse plan merkez UYDURMAZ.
    /// <para>Şube seviyesindeki <c>NormalizeSingleFlag(forceOne: true)</c>'dan bilinçli FARK: şube grafı bir
    /// şirketin TAM listesidir, şirket grafı ise kısmi gelebilir. Burada birini merkez ilan etmek, DB'deki
    /// gerçek merkezi kimsenin istemediği bir anda değiştirmek olurdu.</para></summary>
    [Fact]
    public void No_headquarters_flag_does_not_invent_one()
    {
        var plan = TenantCompanyGraphPlanner.Plan(new List<CompanyGraphDto>
        {
            Node("A", isHeadquarters: false),
            Node("B", isHeadquarters: false),
        });

        plan.ShouldAllBe(s => !s.Company.IsHeadquarters);
    }

    /// <summary>BOŞ graf hiçbir şey yapmaz — "tüm şirketleri sil" DEĞİL.
    /// <para>Yalnız adı değiştiren bir çağrı (ya da alan eklenmeden yazılmış eski bir istemci) bu listeyi
    /// doldurmaz. Boşu silme emri saymak, tek alan güncellemesiyle tenant'ın org ağacını yok etmek olurdu.</para></summary>
    [Fact]
    public void Empty_graph_plans_no_work()
    {
        TenantCompanyGraphPlanner.Plan(new List<CompanyGraphDto>()).ShouldBeEmpty();
    }

    private static CompanyGraphDto Node(string code, bool isHeadquarters) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = code,
        IsHeadquarters = isHeadquarters,
        IsActive = true,
    };
}
