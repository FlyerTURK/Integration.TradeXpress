using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Integration.Framework.Base.Dtos;

namespace Integration.Framework.Base.Querying;

/// <summary>
/// MERKEZİ STANDART: bir <see cref="IQueryable{T}"/> üzerine
/// <see cref="ListRequestDto"/>'nun filtre + sıralama + global aramasını
/// <b>güvenli</b> şekilde uygular.
///
/// <para><b>Güvenlik:</b> alan adları yalnızca bir whitelist'e (varsayılan:
/// <typeparamref name="T"/>'nin public property'leri) veya caller tarafından
/// sağlanan <c>aliases</c> sözlüğüne karşı doğrulanır. Alias expression'ları
/// sunucu tarafında önceden derlenmiştir; client yalnızca alan adı + operatör +
/// değer gönderir. Bilinmeyen alan → <see cref="ListQueryException"/>.</para>
///
/// <para><b>Alias dictionary:</b> navigation property path'leri (ör.
/// <c>x.FollowingUnit.Code</c>) "FriendlyName" → <see cref="LambdaExpression"/>
/// olarak map'lenir. Alias key'leri kendi whitelist'ini oluşturur; ayrıca
/// <c>allowedFields</c>'a eklenmesi gerekmez.</para>
///
/// <para><b>Çeviri:</b> hiç sunucu kodu vendor (DevExpress) tipine dokunmaz;
/// grid durumu presentation adapter'ında <see cref="ListRequestDto"/>'ya çevrilir.</para>
/// </summary>
public static class ListQueryableExtensions
{
    // Savunma sınırları (client-controlled girdiye karşı).
    public const int MaxAllowedResultCount = 200;
    public const int MaxFilters            = 12;
    public const int MaxSorts              = 8;
    public const int MaxSearchLength       = 100;

    private static readonly IReadOnlyDictionary<string, LambdaExpression> EmptyAliases =
        new Dictionary<string, LambdaExpression>(StringComparer.OrdinalIgnoreCase);

    public static IQueryable<T> ApplyListRequest<T>(
        this IQueryable<T> query,
        ListRequestDto request,
        ISet<string>? allowedFields = null,
        IReadOnlyDictionary<string, LambdaExpression>? aliases = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(request);

        NormalizeAndGuard(request);

        var allowed = ResolveAllowed<T>(allowedFields);
        var al = aliases ?? EmptyAliases;

        // 1) Kolon filtreleri (AND)
        foreach (var f in request.Filters ?? Enumerable.Empty<FilterField>())
            query = ApplyFilter(query, f, allowed, al);

        // 1b) IsActive scalar filtresi. Alias'ta veya whitelist'te varsa uygulanır.
        if (request.IsActive is { } isActive &&
            (allowed.ContainsKey("IsActive") || al.ContainsKey("IsActive")))
        {
            query = ApplyFilter(
                query,
                new FilterField { Field = "IsActive", Operator = ListFilterOperator.Equals, Value = isActive ? "true" : "false" },
                allowed, al);
        }

        // 2) Global arama → metinsel alanlarda OR-Contains (doğrudan + alias string alanları)
        if (!string.IsNullOrWhiteSpace(request.Filter))
            query = ApplyGlobalSearch(query, request.Filter!, allowed, al);

        // 3) Sıralama (+ stabil sayfalama için Id tie-breaker)
        query = ApplySorting(query, request, allowed, al);

        return query;
    }

    // ── Savunma: paging/şekil sınırlandırma ───────────────────────────────────

    /// <summary>Sayfalamayı uygular. <see cref="ListRequestDto.AllPages"/> ise TAKE YAPMAZ — tüm kayıtlar döner.
    /// Elle <c>Skip/Take</c> yazan her servis bunu kullanmalı: <c>Take(-1)</c> istisna atmaz, SESSİZCE 0 satır
    /// döndürür — yani sentinel elle yazılan yerlerde fark edilmeden veri kaybına dönüşür.</summary>
    public static IQueryable<T> ApplyPaging<T>(this IQueryable<T> query, ListRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(request);

        // AllPages = HİÇ sayfalama: ne Skip ne Take. "Hepsi" istenirken offset'in anlamı yoktur.
        // <= 0 KONTROLÜ (== -1 DEĞİL): 0 ve diğer negatifler de "hepsi" demektir. Yalnız -1'e bakarsak
        // 0 gelen istek kırpma dalına düşüp Math.Clamp(0,1,200) = 1 olur → SESSİZCE tek satır döner.
        if (request.MaxResultCount <= 0)
        {
            return query;
        }

        return query.Skip(request.SkipCount).Take(request.MaxResultCount);
    }

    /// <summary>Bellek-içi (IEnumerable) karşılığı — ABP/Identity gibi zaten materyalize gelen listeler için.
    /// Aynı <see cref="ListRequestDto.AllPages"/> semantiği: -1 ise Take yok.</summary>
    public static IEnumerable<T> ApplyPaging<T>(this IEnumerable<T> source, ListRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);

        if (request.MaxResultCount <= 0)
        {
            return source;
        }

        return source.Skip(request.SkipCount).Take(request.MaxResultCount);
    }

    private static void NormalizeAndGuard(ListRequestDto request)
    {
        // <= 0 gelen her değer AllPages'e NORMALİZE edilir (kırpılmaz): "tümü" açık bir niyettir ve
        // 200'e indirmek onu sessizce yalanlardı. Normalizasyon burada yapılır ki aşağı akıştaki her
        // karşılaştırma tek bir kanonik değerle (-1) çalışsın.
        if (request.MaxResultCount <= 0)
        {
            request.MaxResultCount = ListRequestDto.AllPages;
        }
        else
        {
            request.MaxResultCount = Math.Clamp(request.MaxResultCount, 1, MaxAllowedResultCount);
        }

        if (request.SkipCount < 0)
            request.SkipCount = 0;

        if (request.Filters is { Count: > MaxFilters })
            throw new ListQueryException($"Too many filters (max {MaxFilters}).");
        if (request.Sorts is { Count: > MaxSorts })
            throw new ListQueryException($"Too many sort fields (max {MaxSorts}).");

        if (request.Filter is { Length: > MaxSearchLength })
            request.Filter = request.Filter[..MaxSearchLength];
    }

    // ── Whitelist çözümü ──────────────────────────────────────────────────────

    private static Dictionary<string, PropertyInfo> ResolveAllowed<T>(ISet<string>? allowedFields)
    {
        var props = typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0);

        if (allowedFields is { Count: > 0 })
            props = props.Where(p => allowedFields.Contains(p.Name));

        return props.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
    }

    private static PropertyInfo Resolve(string field, Dictionary<string, PropertyInfo> allowed)
    {
        if (string.IsNullOrWhiteSpace(field) || !allowed.TryGetValue(field.Trim(), out var prop))
            throw new ListQueryException($"Field '{field}' is not allowed for list querying.");
        return prop;
    }

    /// <summary>
    /// Alan adını alias → doğrudan property sıralamasıyla çözer.
    /// Alias key'leri whitelist'ten bağımsız olarak geçerlidir (caller alias yazmak = izin vermek).
    /// </summary>
    private static (Expression Body, Type FieldType) ResolveField<T>(
        string field,
        Dictionary<string, PropertyInfo> allowed,
        IReadOnlyDictionary<string, LambdaExpression> aliases,
        ParameterExpression param)
    {
        var trimmed = (field ?? string.Empty).Trim();

        // Alias → pre-built sunucu expression; client sadece anahtar adını gönderir.
        if (aliases.TryGetValue(trimmed, out var aliasLambda))
        {
            var body = ParameterReplacer.Replace(aliasLambda.Body, aliasLambda.Parameters[0], param);
            return (body, aliasLambda.ReturnType);
        }

        // Doğrudan entity property (whitelist'te olmalı).
        var prop = Resolve(trimmed, allowed);
        return (Expression.MakeMemberAccess(param, prop), prop.PropertyType);
    }

    // ── Sıralama ──────────────────────────────────────────────────────────────

    private static IQueryable<T> ApplySorting<T>(
        IQueryable<T> query,
        ListRequestDto request,
        Dictionary<string, PropertyInfo> allowed,
        IReadOnlyDictionary<string, LambdaExpression> aliases)
    {
        var orderedProps = new List<PropertyInfo>();
        var ordered = false;

        var sorts = (request.Sorts is { Count: > 0 })
            ? request.Sorts
            : ParseAbpSorting(request.Sorting);

        foreach (var s in sorts)
        {
            var trimmed = (s.Field ?? string.Empty).Trim();

            if (aliases.TryGetValue(trimmed, out var aliasLambda))
            {
                query = ApplyOrderByLambda(query, aliasLambda, s.Descending, thenBy: ordered);
            }
            else
            {
                var prop = Resolve(trimmed, allowed);
                query = ApplyOrder(query, prop, s.Descending, thenBy: ordered);
                orderedProps.Add(prop);
            }
            ordered = true;
        }

        // Stabil sayfalama: Id tie-breaker (alias üzerinden Id sıralaması desteklenmez — Id doğrudan olmalı).
        if (allowed.TryGetValue("Id", out var idProp) && !orderedProps.Contains(idProp))
            query = ApplyOrder(query, idProp, descending: false, thenBy: ordered);

        return query;
    }

    private static IEnumerable<SortField> ParseAbpSorting(string? sorting)
    {
        if (string.IsNullOrWhiteSpace(sorting))
            yield break;

        foreach (var part in sorting.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var tokens = part.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) continue;
            var desc = tokens.Length > 1 && tokens[1].Equals("DESC", StringComparison.OrdinalIgnoreCase);
            yield return new SortField { Field = tokens[0], Descending = desc };
        }
    }

    private static IQueryable<T> ApplyOrder<T>(
        IQueryable<T> query, PropertyInfo prop, bool descending, bool thenBy)
    {
        var param = Expression.Parameter(typeof(T), "x");
        var body = Expression.MakeMemberAccess(param, prop);
        var keySelector = Expression.Lambda(body, param);
        return InvokeOrderMethod(query, keySelector, prop.PropertyType, descending, thenBy);
    }

    private static IQueryable<T> ApplyOrderByLambda<T>(
        IQueryable<T> query, LambdaExpression keySelector, bool descending, bool thenBy)
        => InvokeOrderMethod(query, keySelector, keySelector.ReturnType, descending, thenBy);

    private static IQueryable<T> InvokeOrderMethod<T>(
        IQueryable<T> query, LambdaExpression keySelector, Type keyType, bool descending, bool thenBy)
    {
        var methodName = thenBy
            ? (descending ? "ThenByDescending" : "ThenBy")
            : (descending ? "OrderByDescending" : "OrderBy");

        var method = typeof(Queryable).GetMethods()
            .First(m => m.Name == methodName && m.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(T), keyType);

        return (IQueryable<T>)method.Invoke(null, new object[] { query, keySelector })!;
    }

    // ── Global arama ──────────────────────────────────────────────────────────

    private static IQueryable<T> ApplyGlobalSearch<T>(
        IQueryable<T> query,
        string term,
        Dictionary<string, PropertyInfo> allowed,
        IReadOnlyDictionary<string, LambdaExpression> aliases)
    {
        var param = Expression.Parameter(typeof(T), "x");
        var needle = Expression.Constant(SearchNormalizer.Fold(term));
        Expression? body = null;

        // Doğrudan string property'ler.
        foreach (var prop in allowed.Values.Where(p => p.PropertyType == typeof(string)).Distinct())
        {
            var folded = BuildFoldExpression(Expression.MakeMemberAccess(param, prop));
            body = OrElse(body, Expression.Call(folded, StringContainsMethod, needle));
        }

        // String alias'lar (navigation property path'leri dahil).
        foreach (var aliasLambda in aliases.Values)
        {
            if (aliasLambda.ReturnType != typeof(string)) continue;
            var memberExpr = ParameterReplacer.Replace(aliasLambda.Body, aliasLambda.Parameters[0], param);
            var folded = BuildFoldExpression(memberExpr);
            body = OrElse(body, Expression.Call(folded, StringContainsMethod, needle));
        }

        if (body is null) return query;
        return query.Where(Expression.Lambda<Func<T, bool>>(body, param));
    }

    private static Expression OrElse(Expression? left, Expression right)
        => left is null ? right : Expression.OrElse(left, right);

    // ── Kolon filtresi ────────────────────────────────────────────────────────

    private static IQueryable<T> ApplyFilter<T>(
        IQueryable<T> query,
        FilterField f,
        Dictionary<string, PropertyInfo> allowed,
        IReadOnlyDictionary<string, LambdaExpression> aliases)
    {
        var param = Expression.Parameter(typeof(T), "x");
        var (member, fieldType) = ResolveField<T>(f.Field, allowed, aliases, param);
        var body = BuildFilterBody(member, fieldType, f.Operator, f.Value);
        return query.Where(Expression.Lambda<Func<T, bool>>(body, param));
    }

    private static Expression BuildFilterBody(
        Expression member, Type propType, ListFilterOperator op, string? rawValue)
    {
        switch (op)
        {
            case ListFilterOperator.Contains:
            case ListFilterOperator.StartsWith:
            case ListFilterOperator.EndsWith:
                if (propType != typeof(string))
                    throw new ListQueryException(
                        $"Operator '{op}' is only valid on string fields.");
                return op switch
                {
                    ListFilterOperator.Contains   => BuildFoldedStringOp(member, nameof(string.Contains),   rawValue),
                    ListFilterOperator.StartsWith => BuildFoldedStringOp(member, nameof(string.StartsWith), rawValue),
                    _                             => BuildFoldedStringOp(member, nameof(string.EndsWith),   rawValue),
                };

            default:
                var constant = Expression.Constant(ConvertValue(rawValue, propType), propType);
                return op switch
                {
                    ListFilterOperator.Equals             => Expression.Equal(member, constant),
                    ListFilterOperator.NotEquals          => Expression.NotEqual(member, constant),
                    ListFilterOperator.GreaterThan        => Expression.GreaterThan(member, constant),
                    ListFilterOperator.GreaterThanOrEqual => Expression.GreaterThanOrEqual(member, constant),
                    ListFilterOperator.LessThan           => Expression.LessThan(member, constant),
                    ListFilterOperator.LessThanOrEqual    => Expression.LessThanOrEqual(member, constant),
                    _ => throw new ListQueryException($"Unsupported operator '{op}'."),
                };
        }
    }

    // ── Aksan/harf katlama (fold) — Türkçe-duyarlı, EF-çevrilebilir ───────────

    private static readonly MethodInfo ReplaceMethod =
        typeof(string).GetMethod(nameof(string.Replace), new[] { typeof(string), typeof(string) })!;

    private static readonly MethodInfo FoldToLowerMethod =
        typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!;

    private static readonly MethodInfo StringContainsMethod =
        typeof(string).GetMethod(nameof(string.Contains), new[] { typeof(string) })!;

    private static Expression BuildFoldExpression(Expression member)
    {
        Expression expr = Expression.Coalesce(member, Expression.Constant(string.Empty));
        foreach (var (from, to) in SearchNormalizer.FoldReplacements)
            expr = Expression.Call(expr, ReplaceMethod, Expression.Constant(from), Expression.Constant(to));
        return Expression.Call(expr, FoldToLowerMethod);
    }

    private static Expression BuildFoldedStringOp(Expression member, string methodName, string? rawValue)
    {
        var method = typeof(string).GetMethod(methodName, new[] { typeof(string) })!;
        var folded = BuildFoldExpression(member);
        var needle = Expression.Constant(SearchNormalizer.Fold(rawValue));
        return Expression.Call(folded, method, needle);
    }

    // ── Değer çevrimi (string → alanın gerçek tipi) ───────────────────────────

    private static object? ConvertValue(string? raw, Type propType)
    {
        var underlying = Nullable.GetUnderlyingType(propType) ?? propType;
        var nullable = !propType.IsValueType || Nullable.GetUnderlyingType(propType) is not null;

        if (string.IsNullOrEmpty(raw))
        {
            if (nullable) return null;
            throw new ListQueryException("A value is required for the specified field.");
        }

        try
        {
            if (underlying == typeof(string)) return raw;
            if (underlying.IsEnum) return Enum.Parse(underlying, raw, ignoreCase: true);
            if (underlying == typeof(Guid)) return Guid.Parse(raw);
            if (underlying == typeof(bool)) return bool.Parse(raw);
            if (underlying == typeof(DateTime))
                return DateTime.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            if (underlying == typeof(DateTimeOffset))
                return DateTimeOffset.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            return Convert.ChangeType(raw, underlying, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            // İç tip adı ve ham değer loga bırakılır, istemciye sızdırılmaz.
            throw new ListQueryException("Invalid value for the specified field.");
        }
    }

    // ── ParameterReplacer: alias lambda'nın param'ını bizim param'ımızla değiştirir ──

    private sealed class ParameterReplacer : ExpressionVisitor
    {
        private readonly ParameterExpression _old, _new;

        private ParameterReplacer(ParameterExpression old, ParameterExpression @new)
        {
            _old = old;
            _new = @new;
        }

        public static Expression Replace(Expression body, ParameterExpression old, ParameterExpression @new)
            => new ParameterReplacer(old, @new).Visit(body)!;

        protected override Expression VisitParameter(ParameterExpression node)
            => node == _old ? _new : base.VisitParameter(node);
    }
}
