using System;
using Volo.Abp;

namespace Integration.TradeXpress.MultiCompany;

/// <summary>
/// Şirkete-ait (<c>ICompanyOwned</c>) kayıtların YAZMA sınırı — sahiplik client'tan DEĞİL, aktif working
/// company'den belirlenir (fail-closed).
///
/// <para><b>Neden (kod-inceleme + güvenlik denetimi bulgusu):</b> emtia AppService'leri sahibi
/// <c>createInput.CompanyId</c>'den alıyordu. Bu fail-OPEN'dır: istemci alanı boş bırakınca "holding" (CompanyId=null)
/// kaydı doğuyor ve görünürlük yüklemi holding satırını tenant'ın TÜM şirketlerine gösterdiğinden, bir şirketin
/// kullanıcısı kardeş şirketleri de etkileyen kayıt üretebiliyor/düzenleyebiliyordu (cross-company manipülasyon);
/// istemci başka bir şirketin id'sini göndererek de sahiplik atayabiliyordu. Sahiplik bir GÜVENLİK kararıdır,
/// istemci girdisi değildir.</para>
/// </summary>
public static class CompanyOwnershipGuard
{
    /// <summary>Yeni kaydın sahibi = aktif working company. Aktif şirket yoksa (host/konsolide bağlam) yazma
    /// REDDEDİLİR — sahipsiz (holding) emtia kaydı artık üretilemez.</summary>
    public static Guid ResolveOwnerCompanyId(ICurrentCompany currentCompany)
    {
        if (currentCompany.Id is not { } companyId || companyId == Guid.Empty)
        {
            throw new BusinessException("TradeXpress:MultiCompany:WorkingCompanyRequired");
        }

        return companyId;
    }
}
