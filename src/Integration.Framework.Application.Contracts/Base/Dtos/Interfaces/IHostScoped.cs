namespace Integration.Framework.Base.Dtos.Interfaces;

/// <summary>
/// Host (global, TenantId=null) olabilen List DTO'larını işaretler. <see cref="IsGlobal"/>=true kayıt host
/// kataloğuna aittir → tenant onu düzenleyemez/silemez. Toolbar bunu implement eden TListDto için, tenant
/// oturumunda seçimde global kayıt varsa Sil butonunu pasifleştirir.
/// </summary>
public interface IHostScoped
{
    bool IsGlobal { get; }
}
