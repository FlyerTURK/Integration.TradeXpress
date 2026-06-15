using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevExpress.Blazor;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Querying;
using Volo.Abp.Application.Dtos;

namespace Integration.Framework.Blazor.Client.Components.Crud;

/// <summary>
/// DxGrid'in server-side paging callback'lerini nötr <see cref="ListRequestDto"/>
/// sözleşmesine bağlayan adapter. DevExpress tipleri BURADA biter; sunucuya yalnız
/// vendor-agnostik DTO gider (bkz. mimari kararı: DevExpress yalnız presentation kenarında).
///
/// <para><b>Faz-1 kapsamı:</b> paging (StartIndex/Count) + çok-kolon sıralama (SortInfo)
/// + global arama (<see cref="SearchText"/>). Kolon-filtre (FilterCriteria) ve gruplama
/// bilinçli olarak <i>uygulanmaz</i>; generic grid'de bu affordance'lar kapalıdır
/// (sunucunun gerçekten cevapladığı kadarını aç kuralı).</para>
///
/// <para><b>Tek-fetch cache:</b> DxGrid her yüklemede ayrı <c>GetItemCount</c> +
/// <c>GetItems</c> çağırır; ABP <see cref="PagedResultDto{T}"/> ikisini tek yanıtta
/// döndürdüğü için ilk fetch cache'lenir, eşleşen ikinci prob bellekten servis edilir.</para>
/// </summary>
public sealed class GridListDataSource<TListDto> : GridCustomDataSource
    where TListDto : class
{
    // AppGrid/CrudLayout varsayılan PageSize ile aynı tutulmalı ki ilk GetItems
    // probu cache'e düşsün (ekstra round-trip yok).
    private const int DefaultPrefetchSize = 20;

    private readonly Func<ListRequestDto, Task<PagedResultDto<TListDto>>> _fetch;

    private CacheEntry? _cache;
    private sealed record CacheEntry(ListRequestDto Request, IReadOnlyList<TListDto> Items, long TotalCount);

    public GridListDataSource(Func<ListRequestDto, Task<PagedResultDto<TListDto>>> fetch)
        => _fetch = fetch ?? throw new ArgumentNullException(nameof(fetch));

    /// <summary>Grid veri yükleme hatalarında çağrılır. DevExpress GridCustomDataSource exception'ı
    /// internal yakalayıp sessizce boş grid gösterir; bu callback olmadan hata kullanıcıya/panele ulaşmaz.</summary>
    public Func<Exception, Task>? OnError { get; set; }

    /// <summary>Global arama metni. Sayfa set edip grid'i <c>Reload()</c> eder.</summary>
    public string? SearchText { get; set; }

    /// <summary>IsActive filtresi. null = filtre yok (tüm satırlar), true = yalnız aktif, false = yalnız pasif.</summary>
    public bool? ActiveFilter { get; set; }

    // GridCustomDataSource init sırasında item-tipini tespit için ekstra bir JS→.NET
    // isteği atar; tipi açıkça vererek o round-trip'i (ve DynamicComponent içinde
    // sessiz boş-grid tuzağını) tamamen elemiş oluruz.
    protected override Type DataItemType => typeof(TListDto);

    public override async Task<int> GetItemCountAsync(
        GridCustomDataSourceCountOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var request = BuildRequest(0, DefaultPrefetchSize, sortInfo: null);
            var result  = await _fetch(request);
            _cache      = new CacheEntry(request, result.Items, result.TotalCount);
            return (int)Math.Min(result.TotalCount, int.MaxValue);
        }
        catch (Exception ex)
        {
            if (OnError != null) await OnError(ex);
            return 0;
        }
    }

    public override async Task<IList> GetItemsAsync(
        GridCustomDataSourceItemsOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var request = BuildRequest(options.StartIndex, options.Count, options.SortInfo);

            if (_cache is { } c && SameRequest(c.Request, request))
                return c.Items.ToList();

            var result = await _fetch(request);
            _cache     = new CacheEntry(request, result.Items, result.TotalCount);
            return result.Items.ToList();
        }
        catch (Exception ex)
        {
            if (OnError != null) await OnError(ex);
            return new List<TListDto>();
        }
    }

    private ListRequestDto BuildRequest(
        int skip, int count, IReadOnlyList<GridCustomDataSourceSortInfo>? sortInfo)
        => new()
        {
            SkipCount      = skip < 0 ? 0 : skip,
            MaxResultCount = count <= 0 ? DefaultPrefetchSize : count,
            Filter         = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
            Sorts          = sortInfo?
                .Select(s => new SortField { Field = s.FieldName, Descending = s.DescendingSortOrder })
                .ToList() ?? new List<SortField>(),
            // IsActive: scalar olarak taşınır (GET query-string). Karmaşık List<FilterField> ABP HTTP
            // proxy'sinde GET'te serialize OLMUYOR (Filters[0]=<typeName>); scalar bool? sorunsuz gider.
            IsActive       = ActiveFilter,
        };

    private static bool SameRequest(ListRequestDto a, ListRequestDto b)
        => a.SkipCount == b.SkipCount
        && a.MaxResultCount == b.MaxResultCount
        && string.Equals(a.Filter, b.Filter, StringComparison.Ordinal)
        && SortKey(a) == SortKey(b)
        && a.IsActive == b.IsActive;

    private static string SortKey(ListRequestDto r)
        => string.Join(",", r.Sorts.Select(s => $"{s.Field}:{(s.Descending ? "D" : "A")}"));
}
