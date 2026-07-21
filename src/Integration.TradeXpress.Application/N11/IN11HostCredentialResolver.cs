using System.Threading.Tasks;

namespace Integration.TradeXpress.N11;

/// <summary>N11 host kimliği çözücü — kademeli (host config → müsait aktif N11 kanal hesabı). Mahalle/şehir gibi
/// host-global N11 referans çağrılarının kredensiyel kaynağı. Detay: <see cref="N11HostCredentialResolver"/>.</summary>
public interface IN11HostCredentialResolver
{
    /// <summary>N11 API kimliğini çözer (host config → mevcut scope kanalı → müsait herhangi bir aktif kanal).
    /// Hiçbiri yoksa <c>TradeXpress:N11:NoCredentialsAvailable</c> fırlatır.</summary>
    Task<(string AppKey, string AppSecret)> ResolveAsync();
}
