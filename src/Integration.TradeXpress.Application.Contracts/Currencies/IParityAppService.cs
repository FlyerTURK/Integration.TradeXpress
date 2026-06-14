using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Currencies;

/// <summary>
/// Parite panosu — aktif paritelerin canlı çapraz kurlarını (birimlerin efektif fiyatından)
/// verir. Görünürlük null‖own; oran saf çapraz (Parity marjı yok).
/// </summary>
public interface IParityAppService : IApplicationService
{
    /// <summary>Görünür + aktif pariteler, canlı çapraz kurla. Fiyatı olmayan çiftler atlanır.</summary>
    Task<List<ParityBoardDto>> GetBoardAsync();
}
