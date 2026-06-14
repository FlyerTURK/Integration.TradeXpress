using Integration.Framework;
using Volo.Abp;

namespace Integration.Framework.Base.Querying;

/// <summary>
/// Liste sorgusu izin verilmeyen bir alana / sınır dışı bir şekle (çok fazla
/// filtre vb.) rastladığında fırlatılır. "Fail-loud": sessizce yok saymak yerine
/// açıkça reddeder; bu, presentation'dan bağımsız sunucu-tarafı savunma katmanıdır.
///
/// <para><see cref="BusinessException"/>'dan türer → ABP bunu temiz bir HTTP
/// hatasına (500 değil) çevirir, sunucu iç detayını sızdırmaz; saldırgan
/// alan-probe'ları 500 fırtınası üretmez.</para>
/// </summary>
public sealed class ListQueryException : BusinessException
{
    public ListQueryException(string message)
        : base(FrameworkErrorCodes.ListQueryRejected, message)
    {
    }
}
