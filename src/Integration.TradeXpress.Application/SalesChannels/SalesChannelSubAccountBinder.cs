using System;
using System.Threading.Tasks;
using Integration.TradeXpress.Accounts;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.SalesChannels;

/// <summary>
/// Kanalın muhasebe cari alt hesabını bağlarken yapılan TEK doğrulama: verilen alt hesap gerçekten var mı.
///
/// <para>Üç kanal servisinin (N11/Trendyol/Etsy) Create ve Update yollarında aynı kontrol tekrarlanacağı için
/// tek yere alındı — biri güncellenip diğeri unutulursa kanal yabancı/silinmiş bir cariye bağlanmış görünürdü.</para>
///
/// <para>Şirket sınırı ayrıca zorlanmaz: <c>SubAccount</c> company query-filter'ı altında yaşadığı için başka
/// şirketin alt hesabı bu sorgudan zaten DÖNMEZ ve "bulunamadı" hatasına düşer.</para>
/// </summary>
public class SalesChannelSubAccountBinder : ITransientDependency
{
    private readonly IRepository<SubAccount, Guid> _subAccountRepository;

    public SalesChannelSubAccountBinder(IRepository<SubAccount, Guid> subAccountRepository)
    {
        _subAccountRepository = subAccountRepository;
    }

    /// <summary>Alt hesabı doğrulayıp kanala bağlar. <c>null</c> = bağı çöz (tanımsız bırak).</summary>
    public virtual async Task BindAsync(SalesChannelBase channel, Guid? subAccountId)
    {
        if (subAccountId is { } id && id != Guid.Empty && await _subAccountRepository.FindAsync(id) is null)
        {
            throw new BusinessException("TradeXpress:SalesChannel:SubAccountNotFound");
        }

        channel.SetSubAccount(subAccountId);
    }
}
