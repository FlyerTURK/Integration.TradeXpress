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
