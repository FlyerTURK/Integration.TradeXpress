using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.TradeXpress.Vouchers;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Confirmations;

/// <summary>
/// Teyit (organizasyon-içi karşılıklı ayna onayı) servisi. Karşı taraf bir iç kasa olduğunda process
/// HEMEN postlanmaz: <see cref="ProposeAsync"/> ile Teyit doğar; alıcı <see cref="DeclareAsync"/> ile
/// KENDİ satırını KENDİ ELİYLE yazar (sunucu TAM ayna doğrular); gönderen <see cref="ConfirmAsync"/> ile
/// teyit edince iki bacak atomik postlanır. <b>İptal yoktur</b> — süreci yalnız alıcı
/// <see cref="RejectAsync"/> ile durdurur.
/// </summary>
public interface IConfirmationAppService : IApplicationService
{
    /// <summary>TEKLİF: gönderen kendi TAM satırını yazar (postlama YOK) → alıcının GELEN'ine düşer.</summary>
    Task<ConfirmationDto> ProposeAsync(ProposeConfirmationInput input);

    /// <summary>BEYAN: alıcı KENDİ ELİYLE kendi TAM satırını yazar; sunucu AYNA doğrular (emtia/varyant/miktar/
    /// tutar/birimler aynı, yön ZIT). Tutmazsa uyuşmazlık hatası — teyit açılmaz.</summary>
    Task<ConfirmationDto> DeclareAsync(DeclareConfirmationInput input);

    /// <summary>TEYİT: gönderen alıcının kaydını teyit eder → iki ayna bacak atomik postlanır.</summary>
    Task<ConfirmationDto> ConfirmAsync(ConfirmConfirmationInput input);

    /// <summary>RED: alıcı kabul etmez → durum kapanır (postlanmış bacak yok).</summary>
    Task<ConfirmationDto> RejectAsync(RejectConfirmationInput input);

    /// <summary>Gelen/Giden kutusu: kullanıcının başlatan (giden) ya da karşı (gelen) tarafta olduğu teyitler.</summary>
    Task<List<ConfirmationDto>> GetListAsync(ConfirmationListRequest input);

    /// <summary>Bir tarafın KENDİ eliyle yazdığı satırı döndürür (denetim / uyuşmazlık incelemesi).
    /// <para><b>Kullanım sınırı:</b> alıcının beyan ekranını ÖN-DOLDURMAK için ÇAĞRILMAZ — ön-doldurma
    /// teyidin anlamını öldürür (spec §6: sistem aynalamaz, herkes kendi gerçeğini yazar). Yalnız
    /// kapanmış/uyuşmazlık kayıtlarının incelenmesi içindir.</para></summary>
    Task<VoucherLineDto?> GetPayloadAsync(Guid id, bool initiatorSide);
}
