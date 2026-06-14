using System;
using System.Collections.Generic;
using System.Linq;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Querying;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Querying;

/// <summary>
/// AppService'in <c>GetListAsync</c> boru hattını (ApplyListRequest → Skip/Take → Count)
/// bellek-içi çok satırla doğrular. EF/DB gerekmez: server-side grid'in paging + filtre +
/// sıralama + toplam-sayım davranışı tek satırlık veriyle gözle test edilemediği için
/// burada sentetik veri kümesiyle sabitlenir.
/// </summary>
public class ServerPagingPipelineTests
{
    private sealed class Row
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    private static Guid G(int n) => new($"00000000-0000-0000-0000-{n:D12}");

    // 25 kayıt: Name = "Item-01".."Item-25", tek sayılar aktif.
    private static IQueryable<Row> Data() =>
        Enumerable.Range(1, 25)
            .Select(i => new Row { Id = G(i), Name = $"Item-{i:D2}", IsActive = i % 2 == 1 })
            .ToList()
            .AsQueryable();

    private static readonly HashSet<string> Allowed =
        new(StringComparer.OrdinalIgnoreCase) { "Name", "IsActive", "Id" };

    // AppService.GetListAsync ile aynı boru hattı.
    private static (List<Row> Page, long Total) RunPage(ListRequestDto req)
    {
        var filtered = Data().ApplyListRequest(req, Allowed);
        var total = filtered.LongCount();
        var page = filtered.Skip(req.SkipCount).Take(req.MaxResultCount).ToList();
        return (page, total);
    }

    [Fact]
    public void Second_page_returns_correct_slice_and_full_total()
    {
        var (page, total) = RunPage(new ListRequestDto { SkipCount = 10, MaxResultCount = 10 });

        total.ShouldBe(25);                 // toplam paging'ten etkilenmez
        page.Count.ShouldBe(10);
        page.First().Name.ShouldBe("Item-11");
        page.Last().Name.ShouldBe("Item-20");
    }

    [Fact]
    public void Last_partial_page_returns_remaining_rows()
    {
        var (page, total) = RunPage(new ListRequestDto { SkipCount = 20, MaxResultCount = 10 });

        total.ShouldBe(25);
        page.Count.ShouldBe(5);             // 21..25
        page.First().Name.ShouldBe("Item-21");
        page.Last().Name.ShouldBe("Item-25");
    }

    [Fact]
    public void Descending_sort_is_applied_before_paging()
    {
        var req = new ListRequestDto { SkipCount = 0, MaxResultCount = 3 };
        req.Sorts.Add(new SortField { Field = "Name", Descending = true });

        var (page, _) = RunPage(req);

        page.Select(r => r.Name).ShouldBe(new[] { "Item-25", "Item-24", "Item-23" });
    }

    [Fact]
    public void Filter_narrows_total_and_page_reflects_filtered_set()
    {
        var req = new ListRequestDto { SkipCount = 0, MaxResultCount = 100 };
        req.Filters.Add(new FilterField
        {
            Field = "IsActive",
            Operator = ListFilterOperator.Equals,
            Value = "true"
        });

        var (page, total) = RunPage(req);

        total.ShouldBe(13);                 // 1,3,5..25 → 13 aktif
        page.Count.ShouldBe(13);
        page.ShouldAllBe(r => r.IsActive);
    }

    [Fact]
    public void Global_search_filters_then_pages()
    {
        // "Item-2" alt-dizisini içerenler: Item-20..Item-25 = 6 (Item-02/Item-12 İÇERMEZ).
        var req = new ListRequestDto { SkipCount = 0, MaxResultCount = 5, Filter = "Item-2" };

        var (page, total) = RunPage(req);

        total.ShouldBe(6);
        page.Count.ShouldBe(5);             // 6 kayıttan ilk sayfa = 5
        page.ShouldAllBe(r => r.Name.Contains("Item-2"));
    }

    [Fact]
    public void Unknown_sort_field_is_rejected()
    {
        var req = new ListRequestDto { SkipCount = 0, MaxResultCount = 10 };
        req.Sorts.Add(new SortField { Field = "Password", Descending = false });

        Should.Throw<ListQueryException>(() => RunPage(req));
    }
}
