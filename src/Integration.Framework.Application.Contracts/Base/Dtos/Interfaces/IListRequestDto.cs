using Volo.Abp.Application.Dtos;

namespace Integration.Framework.Base.Dtos.Interfaces;

/// <summary>
/// Tüm liste sorgularında (Grid veri çekme isteklerinde) kullanılacak temel arayüz.
/// Paged ve Sorted özellikleri ABP'den kalıtım yoluyla gelir.
/// </summary>
public interface IListRequestDto : IPagedAndSortedResultRequest
{
    // Özel grid filtreleme argümanları buraya eklenebilir.
}
