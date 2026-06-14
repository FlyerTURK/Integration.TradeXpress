using Volo.Abp.Application.Dtos;

namespace Integration.Framework.Base.Dtos.Interfaces;

/// <summary>
/// Sistemdeki tüm "Liste" (Grid) DTO'larının uyması gereken temel arayüz.
/// Sadece listelerde görünmesi gereken (daha hafif) özellikler barındırmalıdır.
/// </summary>
public interface IListDto<TKey> : IEntityDto<TKey>
{
}
