using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos.Interfaces;
using Integration.Framework.Base.Querying;
using Volo.Abp.Application.Dtos;

namespace Integration.Framework.Base.Dtos;

/// <summary>
/// Tüm liste (grid) sorgularının taşıyıcı sözleşmesi — MERKEZİ STANDART.
/// ABP'nin <see cref="PagedAndSortedResultRequestDto"/>'sundan
/// (SkipCount / MaxResultCount / Sorting) kalıtır; üstüne yapılandırılmış
/// çok-kolon sıralama (<see cref="Sorts"/>), kolon filtreleri
/// (<see cref="Filters"/>) ve global arama (<see cref="Filter"/>) ekler.
///
/// <para>Vendor-agnostiktir: DevExpress / WinForms / herhangi bir istemci
/// kendi grid durumunu bu nötr şekle çevirip gönderir.</para>
/// </summary>
public class ListRequestDto : PagedAndSortedResultRequestDto, IListRequestDto
{
    /// <summary>SAYFALAMA YOK — tüm kayıtlar. Grid'in "Tümü" seçeneği (DevExpress <c>ShowAllRows</c>) ve
    /// sınırlı katalog (lookup) okumaları bu değeri gönderir.
    /// <para>Neden gerekliydi: sayfalama <b>her</b> listeye dayatılınca, tam liste isteyen çağrı yerleri
    /// <c>MaxResultCount = 1000</c> yazmaya başladı (61 yer) — ve bu değer sunucuda sessizce 200'e kırpıldığı
    /// için 249 ülkenin 49'u hiçbir combo'da görünmedi. Artık "tam liste" niyeti AÇIKÇA ifade edilir.</para></summary>
    public const int AllPages = -1;

    /// <summary>ABP'nin <c>[Range(1, MaxMaxResultCount)]</c> kısıtı BİLEREK ezildi: pozitif olmayan değerler
    /// (<see cref="AllPages"/> = -1 ve 0) "tümü" anlamına gelir ve <c>ApplyListRequest</c> bunları -1'e
    /// normalize eder. Alt sınır -1'de tutulur: -5 gibi değerler ANLAMSIZDIR ve sessizce kabul edilmek yerine
    /// doğrulamada YÜKSEK SESLE reddedilir. Üst sınır savunması <c>ApplyListRequest</c> tarafında sürer.</summary>
    [Range(AllPages, int.MaxValue)]
    public override int MaxResultCount { get; set; } = DefaultMaxResultCount;

    /// <summary>Global arama metni (tüm metinsel whitelist alanlarında OR-Contains).</summary>
    public string? Filter { get; set; }

    /// <summary>Yapılandırılmış çok-kolon sıralama. Doluysa ABP <c>Sorting</c> string'inin önüne geçer.</summary>
    public List<SortField> Sorts { get; set; } = new();

    /// <summary>Kolon bazlı filtreler (AND'lenir).</summary>
    public List<FilterField> Filters { get; set; } = new();

    /// <summary>IsActive konvansiyon filtresi (scalar — GET query-string'de sorunsuz serialize olur).
    /// null = filtre yok, true = aktif kayıtlar, false = pasif kayıtlar. ApplyListRequest "IsActive"
    /// alanı whitelist'teyse uygular.</summary>
    public bool? IsActive { get; set; }
}
