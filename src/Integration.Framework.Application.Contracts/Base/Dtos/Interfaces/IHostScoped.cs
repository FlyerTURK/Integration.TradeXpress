namespace Integration.Framework.Base.Dtos.Interfaces;

/// <summary>
/// Host (global, TenantId=null) olabilen DTO'ları işaretler. <see cref="IsGlobal"/>=true kayıt host
/// kataloğuna aittir → tenant onu düzenleyemez/silemez. Toolbar bunu implement eden TListDto için, tenant
/// oturumunda seçimde global kayıt varsa Sil butonunu pasifleştirir. Setter, HostCatalogCrudAppService
/// tabanının map sonrası IsGlobal'i tek noktadan doldurabilmesi içindir.
/// </summary>
public interface IHostScoped
{
    bool IsGlobal { get; set; }
}
