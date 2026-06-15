using System.Threading.Tasks;

namespace Integration.Framework.Blazor.Client.Services.Base;

/// <summary>
/// Framework bileşenlerinin (ör. CrudLayout DxGrid) kendi durumlarını (kolon genişlikleri, vs.)
/// üst katmana (uygulama katmanı) kaydetmesini sağlayan soyut servis.
/// </summary>
public interface IUiStateService
{
    Task SaveGridStateAsync(string gridKey, string stateJson);
    Task<string?> GetGridStateAsync(string gridKey);
}
