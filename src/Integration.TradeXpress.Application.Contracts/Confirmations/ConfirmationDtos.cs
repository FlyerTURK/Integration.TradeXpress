using System;
using System.ComponentModel.DataAnnotations;
using Integration.TradeXpress.Vouchers;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Confirmations;

/// <summary>Teyit belgesi (okuma/liste). Kasa/birim id-only; kodlar AppService'te çözülür (DB'de saklanmaz).
/// Payload'lar (her tarafın kendi satırı) listede TAŞINMAZ — gerekirse
/// <see cref="IConfirmationAppService.GetPayloadAsync"/> ile ayrıca istenir.</summary>
public class ConfirmationDto : AuditedEntityDto<Guid>
{
    public Guid InitiatorVaultId { get; set; }
    public string? InitiatorVaultCode { get; set; }

    public Guid CounterpartyVaultId { get; set; }
    public string? CounterpartyVaultCode { get; set; }

    public ProcessType ProcessType { get; set; }

    /// <summary>Gönderenin yönü; alıcının aynası AYNI EKSENDE zıt yöndür.</summary>
    public ProcessDirectionType Direction { get; set; }

    // ── Ayna anahtarı (denormalize) — grid gösterimi + uyuşmazlık teşhiri ──

    public Guid? CommodityId { get; set; }
    public Guid? VariantId { get; set; }
    public decimal Quantity { get; set; }
    public decimal Amount { get; set; }

    /// <summary>Ana bacağın birimi — ana bacağı olmayan tiplerde (Dekont) null.</summary>
    public Guid? MainUnitId { get; set; }
    public string? MainUnitCode { get; set; }

    public Guid? PayUnitId { get; set; }
    public string? PayUnitCode { get; set; }
    public decimal PayTotal { get; set; }

    public ConfirmationStatus Status { get; set; }

    public string? Note { get; set; }
    public string? DecisionNote { get; set; }

    public Guid? InitiatorVoucherId { get; set; }
    public Guid? CounterpartyVoucherId { get; set; }

    /// <summary>UI-gating türetilmiş bayrağı (SSOT: sunucu-otoriter): oturum kullanıcısı BAŞLATAN kasaya
    /// erişebiliyor mu (= GİDEN kutusu tarafı). Buton görünürlüğü içindir — gerçek yetki aksiyon çağrısında
    /// sunucuda TEKRAR enforce edilir.</summary>
    public bool IsInitiatorMine { get; set; }

    /// <summary>UI-gating türetilmiş bayrağı: oturum kullanıcısı KARŞI kasaya erişebiliyor mu
    /// (= GELEN kutusu tarafı). Ayrıntı için <see cref="IsInitiatorMine"/>.</summary>
    public bool IsCounterpartyMine { get; set; }

    public override string ToString()
    {
        return $"{ProcessType} {Direction} {Quantity}/{Amount} [{Status}]";
    }
}

/// <summary>TEKLİF (gönderen kendi satırını yazar): iç kasaya karşı bir process kaydedilir. Postlama YOK —
/// Teyit kaydı <see cref="ConfirmationStatus.Proposed"/> doğar, alıcının GELEN'ine düşer.
/// <para><see cref="Line"/> panelin ürettiği TAM satırdır; sunucu ayna anahtarını ONDAN TÜRETİR (client
/// anahtarı ayrıca göndermez — çift kaynak olmasın). Fiş BAŞLIĞI (hesap/cari) replay EDİLMEZ: teyitte
/// sunucu karşı KASADAN türetir (AccountType=Vault; cari üretilmez).</para></summary>
public class ProposeConfirmationInput
{
    /// <summary>Başlatan (gönderen) kasa — oturum kullanıcısının yetkili olduğu kasa.</summary>
    public Guid InitiatorVaultId { get; set; }

    /// <summary>Karşı iç kasa (alıcı).</summary>
    public Guid CounterpartyVaultId { get; set; }

    /// <summary>Gönderenin KENDİ eliyle yazdığı tam process satırı.</summary>
    [Required]
    public VoucherLineDto Line { get; set; } = new();

    [StringLength(ConfirmationConsts.NoteMaxLength)]
    public string? Note { get; set; }
}

/// <summary>BEYAN (alıcı KENDİ ELİYLE kendi satırını yazar — sistem aynalamaz). Alıcı buraya KENDİ gözlediği
/// değerlerle TAM bir process satırı girer; sunucu bunun gönderenin satırıyla AYNA olduğunu doğrular
/// (emtia+varyant+miktar+tutar+ana birim+karşılık birimi+karşılık tutarı, ZIT yön). Tutmazsa teyit açılmaz,
/// fark yüzeye çıkar (fire/kayıp dedektörü). Postlama YOK — Proposed→Declared.</summary>
public class DeclareConfirmationInput
{
    public Guid Id { get; set; }

    /// <summary>Alıcının KENDİ eliyle yazdığı tam process satırı (ÖN-DOLDURULMAZ).</summary>
    [Required]
    public VoucherLineDto Line { get; set; } = new();

    [StringLength(ConfirmationConsts.DecisionNoteMaxLength)]
    public string? Note { get; set; }
}

/// <summary>TEYİT (gönderen, alıcının kaydını teyit eder): iki ayna bacak atomik postlanır. Declared→Confirmed.</summary>
public class ConfirmConfirmationInput
{
    public Guid Id { get; set; }

    [StringLength(ConfirmationConsts.DecisionNoteMaxLength)]
    public string? Note { get; set; }
}

/// <summary>RED (alıcı kabul etmez): postlanmış bacak yok, yalnız durum kapanır. Proposed|Declared→Rejected.
/// (Gönderenin İPTALİ YOKTUR — süreci yalnız alıcı durdurabilir.)</summary>
public class RejectConfirmationInput
{
    public Guid Id { get; set; }

    [StringLength(ConfirmationConsts.DecisionNoteMaxLength)]
    public string? Reason { get; set; }
}

/// <summary>Gelen/Giden kutusu isteği — company-scoped, iki-taraflı. <see cref="VaultId"/> verilirse yalnız
/// o kasayla ilgili teyitler (başlatan ya da karşı).</summary>
public class ConfirmationListRequest
{
    /// <summary>Opsiyonel kasa filtresi (null = kullanıcının erişebildiği tüm kasalar).</summary>
    public Guid? VaultId { get; set; }
}
