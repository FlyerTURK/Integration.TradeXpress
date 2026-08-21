using System;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Integration.TradeXpress.SalesChannels;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.ChannelQuestions;

/// <summary>Kanal sorusu gelen-kutusu sorgusu (per-tenant, company-owned) — şirket kapsamını SUNUCU zorlar
/// (client <c>CompanyId</c> GÖNDERMEZ; global query filter uygular). Merkezi <see cref="ListRequestDto"/>
/// standardına ek olarak gelen kutusuna ÖZEL eksenler taşınır: bunlar kolon filtresi değil, ekranın kendi
/// varsayılan görünümünü (bekleyenler/okunmamışlar) kuran tipli anahtarlardır.</summary>
public class ChannelQuestionListRequestDto : ListRequestDto
{
    /// <summary>Tek bir satış kanalına daralt (null = tüm kanallar — ortak gelen kutusu).</summary>
    public Guid? SalesChannelId { get; set; }

    /// <summary>Nötr (kanal-agnostik) soru durumu filtresi.</summary>
    public ChannelQuestionStatus? NeutralStatus { get; set; }

    /// <summary>Cevabın YEREL teslim durumu filtresi (sorunun kanal durumundan bağımsız).</summary>
    public ChannelAnswerState? AnswerState { get; set; }

    /// <summary>Okundu/okunmadı filtresi (null = ikisi de).</summary>
    public bool? IsRead { get; set; }

    /// <summary>Yalnız CEVAP BEKLEYENLER — gelen kutusunun iş listesi. Kanalda hâlâ açık (<see cref="ChannelQuestionStatus.Pending"/>)
    /// VE cevabı pazaryerine gitmemiş satırlar. Taslak/kuyruktaki/başarısız cevaplar hâlâ iş beklediği için
    /// listede KALIR; yalnız gerçekten gönderilmiş olan düşer.</summary>
    public bool OnlyPending { get; set; }
}

/// <summary>Ortak soru gelen-kutusu grid satırı — TÜM kanalların ürün soruları (kanal yalnız discriminator:
/// <see cref="ChannelType"/> + sunucuda çözülen <see cref="SalesChannelName"/>). Satırlar SNAPSHOT'tan çizilir:
/// yerel ürün eşleşmese/silinse bile soru neyi sorduğunu bilir.
///
/// <para><b>"Gönderildi" ibaresi YALNIZ <see cref="AnswerState"/> = <see cref="ChannelAnswerState.Sent"/>
/// satırında gösterilebilir</b> — cevap bugün pazaryerine GÖNDERİLMİYOR, dolayısıyla hiçbir satır o duruma
/// geçmez. <see cref="AnsweredAt"/> (yerelde yazıldığı an) ile <see cref="AnswerPushedAt"/> (gerçekten
/// gönderildiği an) bilerek AYRI taşınır; ikisini tek "cevap tarihi" kolonunda birleştirmek kullanıcıya
/// cevabın gittiğini sandırır ve bu pazaryeri puanında gerçek zarardır.</para></summary>
public class ChannelQuestionListDto : EntityDto<Guid>, IListDto<Guid>
{
    public Guid SalesChannelId { get; set; }

    /// <summary>Kanal türü (discriminator) — grid "Kanal" kolonu + filtre.</summary>
    public SalesChannelType ChannelType { get; set; }

    /// <summary>Kanal adı — AppService'te TEK BATCH enrich edilir (id-only referanstan; mapper doldurmaz).</summary>
    public string? SalesChannelName { get; set; }

    /// <summary>Kanaldaki soru kimliği — idempotency anahtarı; kullanıcıya kanal tarafıyla ortak referans.</summary>
    public string RemoteQuestionId { get; set; } = string.Empty;

    /// <summary>Kanaldaki ürün kimliği (snapshot) — yerel eşleşme kurulamasa da soru hangi ürüne ait bilinir.</summary>
    public string? RemoteProductId { get; set; }

    /// <summary>Eşleşen YEREL ürün — null NORMAL durumdur (eşleşme kurulamamış ya da ürün silinmiş).</summary>
    public Guid? ProductId { get; set; }

    /// <summary>Ürün başlığı snapshot'ı (kanaldan).</summary>
    public string? ProductTitle { get; set; }

    public string? Subject { get; set; }
    public string? QuestionText { get; set; }

    /// <summary>Soruyu soran müşterinin adı — cevabı kişiselleştirmek için gösterilir (gerekçe entity XML doc'unda).</summary>
    public string? CustomerName { get; set; }

    /// <summary>Müşterinin iletişim adresi (kanaldan geldiği gibi).</summary>
    public string? CustomerEmail { get; set; }

    /// <summary>Kanalın bildirdiği soru tarihi. N11'de GÜN hassasiyetindedir (saat yok) → SLA için KULLANILAMAZ;
    /// geri sayım <see cref="FirstSeenAt"/> üzerinden hesaplanır.</summary>
    public DateTime? RemoteQuestionDate { get; set; }

    /// <summary>Soruyu İLK gördüğümüz an (UTC) — SLA geri sayımının TEK güvenilir kaynağı.</summary>
    public DateTime FirstSeenAt { get; set; }

    /// <summary>Bu kaydın en son çekildiği an (UTC) — tazelik göstergesi.</summary>
    public DateTime FetchedAt { get; set; }

    public ChannelQuestionStatus NeutralStatus { get; set; }

    /// <summary>Ham kanal durumu — nötr eşlemenin kaynağı (denetim).</summary>
    public string? RemoteStatus { get; set; }

    /// <summary>Soru/cevap pazaryerinde herkese açık mı. <c>null</c> = BİLİNMİYOR (üç durumlu — doğrulanmadan
    /// "herkese açık" etiketi göstermek müşteri mahremiyeti açısından risklidir).</summary>
    public bool? IsPublic { get; set; }

    public bool IsRead { get; set; }

    /// <summary>YEREL cevap metni (taslak ya da gönderilmiş).</summary>
    public string? AnswerText { get; set; }

    /// <summary>Cevabın TESLİM durumu — sorunun kanal durumundan BAĞIMSIZ (bkz. tip özeti).</summary>
    public ChannelAnswerState AnswerState { get; set; }

    /// <summary>Cevabın YERELDE yazıldığı an (UTC). Gönderim anı DEĞİLDİR.</summary>
    public DateTime? AnsweredAt { get; set; }

    /// <summary>Cevabın pazaryerine GERÇEKTEN gönderildiği an (UTC). Push açılana kadar daima <c>null</c>.</summary>
    public DateTime? AnswerPushedAt { get; set; }

    public override string ToString()
    {
        return RemoteQuestionId;
    }
}

/// <summary>Cevap yazma girdisi. <see cref="ReadyToSend"/> yalnız satırı gönderim SIRASINA alır
/// (<c>ChannelAnswerState.ReadyToSend</c>) — HİÇBİR ŞEY GÖNDERMEZ; push ayrı bir işle açılacak.</summary>
public class ChannelQuestionAnswerInput
{
    /// <summary>Cevap metni. Boş bırakmak cevabı TEMİZLER (entity taslağı sıfırlar).
    /// <para>İçerik kısıtı: pazaryeri müşteriyi kanal DIŞINA davet etmeyi yasaklar (kendi sitemize, başka bir
    /// pazaryerine, harici iletişim kanalına yönlendirme) — cevap yazma ekranı (<c>ChannelQuestionListPage</c>) kullanıcıyı uyarmalıdır.</para></summary>
    [StringLength(ChannelQuestionConsts.AnswerTextMaxLength)]
    public string? AnswerText { get; set; }

    /// <summary>Cevap tamam → gönderim kuyruğuna al (bkz. tip özeti: gönderim YOK, yalnız kuyruk).</summary>
    public bool ReadyToSend { get; set; }
}
