using System;
using System.Collections.Generic;
using System.Linq;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Querying;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Querying;

/// <summary>
/// API'ye sızmaya çalışan client-controlled girdiye karşı savunma sınırlarını
/// (clamp + cap) doğrular. Whitelist alan-enjeksiyonunu, bu testler de paging/şekil
/// kötüye-kullanımını (DoS / expression-bomb) kapatır.
/// </summary>
public class ListQueryGuardTests
{
    private sealed class Row
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private static IQueryable<Row> Data() =>
        Enumerable.Range(1, 5)
            .Select(i => new Row { Id = new Guid($"00000000-0000-0000-0000-{i:D12}"), Name = $"N{i}" })
            .ToList()
            .AsQueryable();

    private static readonly HashSet<string> Allowed =
        new(StringComparer.OrdinalIgnoreCase) { "Name", "Id" };

    [Fact]
    public void Huge_page_size_is_clamped_to_max()
    {
        var req = new ListRequestDto { MaxResultCount = 1_000_000 };
        Data().ApplyListRequest(req, Allowed);
        req.MaxResultCount.ShouldBe(ListQueryableExtensions.MaxAllowedResultCount);
    }

    [Fact]
    public void Nonpositive_page_size_is_clamped_to_one()
    {
        var req = new ListRequestDto { MaxResultCount = 0 };
        Data().ApplyListRequest(req, Allowed);
        req.MaxResultCount.ShouldBe(1);
    }

    [Fact]
    public void Negative_skip_is_clamped_to_zero()
    {
        var req = new ListRequestDto { SkipCount = -50, MaxResultCount = 10 };
        Data().ApplyListRequest(req, Allowed);
        req.SkipCount.ShouldBe(0);
    }

    [Fact]
    public void Too_many_filters_is_rejected()
    {
        var req = new ListRequestDto { MaxResultCount = 10 };
        for (var i = 0; i < ListQueryableExtensions.MaxFilters + 1; i++)
            req.Filters.Add(new FilterField { Field = "Name", Operator = ListFilterOperator.Contains, Value = "x" });

        Should.Throw<ListQueryException>(() => Data().ApplyListRequest(req, Allowed));
    }

    [Fact]
    public void Too_many_sorts_is_rejected()
    {
        var req = new ListRequestDto { MaxResultCount = 10 };
        for (var i = 0; i < ListQueryableExtensions.MaxSorts + 1; i++)
            req.Sorts.Add(new SortField { Field = "Name" });

        Should.Throw<ListQueryException>(() => Data().ApplyListRequest(req, Allowed));
    }

    [Fact]
    public void Overlong_search_text_is_truncated()
    {
        var req = new ListRequestDto { MaxResultCount = 10, Filter = new string('a', 250) };
        Data().ApplyListRequest(req, Allowed);
        req.Filter!.Length.ShouldBe(ListQueryableExtensions.MaxSearchLength);
    }
}
