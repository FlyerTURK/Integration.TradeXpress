using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevExpress.Blazor;
using DevExpress.Data.Filtering;
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
    // Son GetItems request'i (Sorts/Filter/Filters DOLU). FetchSingleAsync bunu kullanır; _cache, GetItemCount'un
    // sort'suz sorgusuyla kirletilebildiği için tek-kayıt sorgusunun sıralaması buradan korunur.
    private ListRequestDto? _lastItemsRequest;
    private sealed record CacheEntry(ListRequestDto Request, IReadOnlyList<TListDto> Items, long TotalCount);

    /// <summary>Son fetch edilen (görünür) sayfanın kayıtları — Previous/Next gezinme için.</summary>
    public IReadOnlyList<TListDto> CurrentItems => _cache?.Items ?? System.Array.Empty<TListDto>();

    /// <summary>Sunucudaki toplam kayıt (sayfa-aşırı gezinme sınır kontrolü için).</summary>
    public long TotalCount => _cache?.TotalCount ?? 0;

    /// <summary>Son fetch'in request'i (SkipCount/Sorts/Filter/IsActive) — tek-kayıt sorgusu aynı sırada gitsin.</summary>
    public ListRequestDto? LastRequest => _cache?.Request;

    /// <summary>Her başarılı GetItems (cache hit dahil) sonrası tetiklenir → CrudLayout StateService'i tazeler
    /// (grid fetch'i CrudLayout'u re-render etmediği için OnAfterRender senkronu güvenilmezdi).</summary>
    public event Action? Fetched;

    /// <summary>Global sıradaki TEK kaydı, grid'in o anki sıralaması/filtresiyle çeker (SkipCount=globalIndex,
    /// MaxResultCount=1). Sayfa-aşırı komşu kaydı bulmak için.</summary>
    public async Task<TListDto?> FetchSingleAsync(int globalIndex)
    {
        if (globalIndex < 0) return null;
        // Son GETITEMS request'ini baz al (Sorts/Filter/Filters DOLU). _cache.Request, GetItemCountAsync'in
        // sort'suz (sortInfo:null) sorgusuyla kirletilebildiği için ona güvenmiyoruz — yoksa tek-kayıt sorgusu
        // sıralamasız (default/Id) sıraya düşüp grid'den farklı sıra üretir.
        var baseReq = _lastItemsRequest ?? _cache?.Request;
        var request = new ListRequestDto
        {
            SkipCount      = globalIndex,
            MaxResultCount = 1,
            Filter         = baseReq?.Filter ?? (string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim()),
            Sorts          = baseReq?.Sorts ?? new List<SortField>(),
            Filters        = baseReq?.Filters ?? new List<FilterField>(),
            IsActive       = baseReq?.IsActive ?? ActiveFilter,
        };
        try
        {
            var result = await _fetch(request);
            return result.Items.FirstOrDefault();
        }
        catch (Exception ex)
        {
            if (OnError != null) await OnError(ex);
            return null;
        }
    }

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
            var request = BuildRequest(0, DefaultPrefetchSize, sortInfo: null, options.FilterCriteria);
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
            var request = BuildRequest(options.StartIndex, options.Count, options.SortInfo, options.FilterCriteria);
            _lastItemsRequest = request;   // FetchSingle bunu baz alır (Sorts/Filter/Filters dolu; sayım sorgusu kirletmez)

            if (_cache is { } c && SameRequest(c.Request, request))
            {
                Fetched?.Invoke();   // cache hit: state senkronu yine de tazele (sayfa/sıra değişmiş olabilir)
                return c.Items.ToList();
            }

            var result = await _fetch(request);
            // Out-of-range/boş sayfa (skip>0 ama 0 kayıt) ÖNCEKİ dolu cache'i ZEHİRLEMESİN: yalnız TotalCount'u
            // tazele, dolu sayfayı (Items/Request) koru. Aksi halde grid out-of-range bir sayfaya giderse
            // CurrentItems kalıcı boşalır → ListDataSource boş → gezinme kilitlenir.
            if (result.Items.Count == 0 && result.TotalCount > 0 && request.SkipCount > 0 && _cache != null)
                _cache = _cache with { TotalCount = result.TotalCount };
            else
                _cache = new CacheEntry(request, result.Items, result.TotalCount);
            Fetched?.Invoke();
            return result.Items.ToList();
        }
        catch (Exception ex)
        {
            if (OnError != null) await OnError(ex);
            return new List<TListDto>();
        }
    }

    private ListRequestDto BuildRequest(
        int skip, int count, IReadOnlyList<GridCustomDataSourceSortInfo>? sortInfo, CriteriaOperator? filterCriteria = null)
        => new()
        {
            SkipCount      = skip < 0 ? 0 : skip,
            MaxResultCount = count <= 0 ? DefaultPrefetchSize : count,
            Filter         = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
            Sorts          = sortInfo?
                .Select(s => new SortField { Field = s.FieldName, Descending = s.DescendingSortOrder })
                .ToList() ?? new List<SortField>(),
            IsActive       = ActiveFilter,
            // Kolon filtreleri: DevExpress CriteriaOperator → nötr FilterField listesi; server ApplyListRequest
            // bunu whitelist'li IQueryable'a çevirir. List<SortField> ABP query-string'de gittiği gibi
            // List<FilterField> de aynı convention'la gider.
            Filters        = CriteriaFilterTranslator.Translate(filterCriteria),
        };

    private static bool SameRequest(ListRequestDto a, ListRequestDto b)
        => a.SkipCount == b.SkipCount
        && a.MaxResultCount == b.MaxResultCount
        && string.Equals(a.Filter, b.Filter, StringComparison.Ordinal)
        && SortKey(a) == SortKey(b)
        && a.IsActive == b.IsActive
        && FilterKey(a) == FilterKey(b);

    private static string SortKey(ListRequestDto r)
        => string.Join(",", r.Sorts.Select(s => $"{s.Field}:{(s.Descending ? "D" : "A")}"));

    private static string FilterKey(ListRequestDto r)
        => string.Join("|", (r.Filters ?? new List<FilterField>()).Select(f => $"{f.Field}:{f.Operator}:{f.Value}"));
}
