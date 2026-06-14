using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Querying;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Querying;

/// <summary>
/// Merkezi <see cref="ListQueryableExtensions"/> standardının saf birim testleri.
/// Bellek-içi IQueryable üstünde çalışır (ABP/EF gerektirmez) — herkesin miras
/// aldığı kritik katman olduğu için filtre/sıralama/whitelist/alias davranışı sabitlenir.
/// </summary>
public class ListQueryableExtensionsTests
{
    // ── Doğrudan property testleri için basit model ────────────────────────────

    private sealed class Person
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public int Age { get; set; }
        public bool IsActive { get; set; }
    }

    private static Guid G(int n) => new($"00000000-0000-0000-0000-{n:D12}");

    private static IQueryable<Person> Data() => new List<Person>
    {
        new() { Id = G(1), Name = "Ahmet",  City = "Ankara",   Age = 30, IsActive = true  },
        new() { Id = G(2), Name = "Mehmet", City = "Istanbul", Age = 25, IsActive = true  },
        new() { Id = G(3), Name = "Ayse",   City = "Ankara",   Age = 40, IsActive = false },
        new() { Id = G(4), Name = "Fatma",  City = "Izmir",    Age = 30, IsActive = true  },
    }.AsQueryable();

    [Fact]
    public void Contains_filter_is_case_insensitive()
    {
        var req = new ListRequestDto
        {
            Filters = { new FilterField { Field = "City", Operator = ListFilterOperator.Contains, Value = "ANK" } }
        };

        var result = Data().ApplyListRequest(req).ToList();

        result.Select(p => p.Name).ShouldBe(new[] { "Ahmet", "Ayse" }); // Id sıralı tie-breaker
    }

    [Fact]
    public void GreaterThan_filter_converts_string_value_to_int()
    {
        var req = new ListRequestDto
        {
            Filters = { new FilterField { Field = "Age", Operator = ListFilterOperator.GreaterThan, Value = "29" } }
        };

        var result = Data().ApplyListRequest(req).Select(p => p.Name).ToList();

        result.ShouldContain("Ahmet");
        result.ShouldContain("Ayse");
        result.ShouldContain("Fatma");
        result.ShouldNotContain("Mehmet");
    }

    [Fact]
    public void Equals_filter_on_bool_works()
    {
        var req = new ListRequestDto
        {
            Filters = { new FilterField { Field = "IsActive", Operator = ListFilterOperator.Equals, Value = "false" } }
        };

        var result = Data().ApplyListRequest(req).ToList();

        result.ShouldHaveSingleItem().Name.ShouldBe("Ayse");
    }

    [Fact]
    public void Global_search_matches_any_string_field()
    {
        var req = new ListRequestDto { Filter = "izm" };

        var result = Data().ApplyListRequest(req).ToList();

        result.ShouldHaveSingleItem().Name.ShouldBe("Fatma"); // City=Izmir
    }

    [Fact]
    public void Structured_sort_is_applied_descending()
    {
        var req = new ListRequestDto
        {
            Sorts = { new SortField { Field = "Age", Descending = true } }
        };

        var ages = Data().ApplyListRequest(req).Select(p => p.Age).ToList();

        ages.ShouldBe(new[] { 40, 30, 30, 25 });
    }

    [Fact]
    public void Id_tiebreaker_makes_paging_deterministic()
    {
        // Age=30 iki kayıt (Ahmet=Id1, Fatma=Id4); Age artan + Id tie-breaker
        var req = new ListRequestDto { Sorts = { new SortField { Field = "Age", Descending = false } } };

        var names = Data().ApplyListRequest(req).Select(p => p.Name).ToList();

        names.ShouldBe(new[] { "Mehmet", "Ahmet", "Fatma", "Ayse" });
    }

    [Fact]
    public void Abp_sorting_string_is_used_when_no_structured_sorts()
    {
        var req = new ListRequestDto { Sorting = "Name DESC" };

        var names = Data().ApplyListRequest(req).Select(p => p.Name).ToList();

        names.ShouldBe(new[] { "Mehmet", "Fatma", "Ayse", "Ahmet" });
    }

    [Fact]
    public void Unknown_field_throws_fail_loud()
    {
        var req = new ListRequestDto
        {
            Filters = { new FilterField { Field = "Salary", Operator = ListFilterOperator.GreaterThan, Value = "1" } }
        };

        Should.Throw<ListQueryException>(() => Data().ApplyListRequest(req).ToList());
    }

    [Fact]
    public void Field_outside_explicit_whitelist_is_rejected()
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Name" };
        var req = new ListRequestDto { Sorts = { new SortField { Field = "Age" } } };

        Should.Throw<ListQueryException>(() => Data().ApplyListRequest(req, allowed).ToList());
    }

    [Fact]
    public void String_operator_on_nonstring_field_throws()
    {
        var req = new ListRequestDto
        {
            Filters = { new FilterField { Field = "Age", Operator = ListFilterOperator.Contains, Value = "3" } }
        };

        Should.Throw<ListQueryException>(() => Data().ApplyListRequest(req).ToList());
    }

    // ── Alias dictionary testleri ──────────────────────────────────────────────
    // Navigation property join'lerini (ör. x.Friend.Code) düz alias adıyla
    // filtreleyebilmek için kullanılır. In-memory testte gerçek nesne referansı
    // yeterli; EF'te aynı expression LEFT JOIN'e çevrilir.

    private sealed class Tag { public string Code { get; set; } = string.Empty; }

    private sealed class Item
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public Tag Category { get; set; } = new();   // in-memory: asla null
    }

    private static IQueryable<Item> Items() => new List<Item>
    {
        new() { Id = G(1), Name = "Elmas", Category = new Tag { Code = "PREC" } },
        new() { Id = G(2), Name = "Gümüş", Category = new Tag { Code = "BASE" } },
        new() { Id = G(3), Name = "Altın", Category = new Tag { Code = "PREC" } },
    }.AsQueryable();

    private static readonly IReadOnlyDictionary<string, LambdaExpression> CategoryAliases =
        new Dictionary<string, LambdaExpression>(StringComparer.OrdinalIgnoreCase)
        {
            ["CategoryCode"] = (Expression<Func<Item, string>>)(x => x.Category.Code)
        };

    [Fact]
    public void Alias_equals_filter_works()
    {
        var req = new ListRequestDto
        {
            Filters = { new FilterField { Field = "CategoryCode", Operator = ListFilterOperator.Equals, Value = "PREC" } }
        };

        var names = Items().ApplyListRequest(req, aliases: CategoryAliases).Select(i => i.Name).ToList();

        names.ShouldContain("Elmas");
        names.ShouldContain("Altın");
        names.ShouldNotContain("Gümüş");
    }

    [Fact]
    public void Alias_contains_filter_is_case_insensitive()
    {
        var req = new ListRequestDto
        {
            Filters = { new FilterField { Field = "CategoryCode", Operator = ListFilterOperator.Contains, Value = "rec" } }
        };

        var count = Items().ApplyListRequest(req, aliases: CategoryAliases).Count();

        count.ShouldBe(2); // PREC × 2
    }

    [Fact]
    public void Alias_sort_works()
    {
        var req = new ListRequestDto
        {
            Sorts = { new SortField { Field = "CategoryCode", Descending = false } }
        };

        // BASE < PREC; tie-breaker Id → Elmas (Id1) önce, Altın (Id3) sonra
        var names = Items().ApplyListRequest(req, aliases: CategoryAliases).Select(i => i.Name).ToList();

        names[0].ShouldBe("Gümüş");   // BASE
        names[1].ShouldBe("Elmas");   // PREC, Id1
        names[2].ShouldBe("Altın");   // PREC, Id3
    }

    [Fact]
    public void Alias_key_is_included_in_global_search()
    {
        // "BASE" CategoryCode'da var — global arama string alias'ları da tarar.
        var req = new ListRequestDto { Filter = "base" };

        var names = Items().ApplyListRequest(req, aliases: CategoryAliases).Select(i => i.Name).ToList();

        names.ShouldHaveSingleItem().ShouldBe("Gümüş");
    }

    [Fact]
    public void Unknown_field_still_throws_when_aliases_present()
    {
        // "Price" ne whitelist'te ne alias'ta → fail-loud
        var req = new ListRequestDto
        {
            Filters = { new FilterField { Field = "Price", Operator = ListFilterOperator.GreaterThan, Value = "0" } }
        };

        Should.Throw<ListQueryException>(() =>
            Items().ApplyListRequest(req, aliases: CategoryAliases).ToList());
    }

    [Fact]
    public void Alias_does_not_require_field_in_allowedFields()
    {
        // allowedFields = sadece "Name"; ama alias "CategoryCode" kendi whitelist'ini oluşturur.
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Name", "Id" };
        var req = new ListRequestDto
        {
            Filters = { new FilterField { Field = "CategoryCode", Operator = ListFilterOperator.Equals, Value = "BASE" } }
        };

        var count = Items().ApplyListRequest(req, allowed, CategoryAliases).Count();

        count.ShouldBe(1); // alias izin verir, allowedFields engellemez
    }

    [Fact]
    public void ConvertValue_error_message_does_not_leak_internal_type_name()
    {
        // Hata mesajı iç tip adı (Guid, Int32 vb.) içermemeli.
        var req = new ListRequestDto
        {
            Filters = { new FilterField { Field = "Id", Operator = ListFilterOperator.Equals, Value = "not-a-guid" } }
        };

        var ex = Should.Throw<ListQueryException>(() => Data().ApplyListRequest(req).ToList());

        ex.Message.ShouldNotContain("Guid");
        ex.Message.ShouldNotContain("not-a-guid");
    }
}
