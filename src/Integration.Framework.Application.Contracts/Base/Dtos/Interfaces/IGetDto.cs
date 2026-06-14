using Volo.Abp.Application.Dtos;

namespace Integration.Framework.Base.Dtos.Interfaces;

/// <summary>
/// Sistemdeki tüm "Okuma / Detay Görüntüleme" (Get) DTO'larının uyması gereken temel arayüz.
/// UI tarafında Edit formları bu DTO'ya bağlanır.
/// İçerisinde UI için gerekli "NonDb" (veritabanında olmayan) özellikleri de barındırır.
/// </summary>
public interface IGetDto<TKey> : IEntityDto<TKey>
{
    /// <summary>
    /// Kaydın pagination yapısında kaçıncı sayfaya düştüğünü belirtir.
    /// UI bu bilgiye göre otomatik sayfa zıplaması yapar.
    /// </summary>
    int PageIndex { get; set; }
}
