using Volo.Abp.Application.Dtos;

namespace Integration.Framework.Base.Dtos.Interfaces;

/// <summary>
/// Sistemdeki tüm "Okuma / Detay Görüntüleme" (Get) DTO'larının uyduğu temel arayüz.
/// UI tarafında Edit formları bu DTO'ya bağlanır (marker; ortak Get davranışı için genişletilebilir).
/// </summary>
public interface IGetDto<TKey> : IEntityDto<TKey>
{
}
