using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Vouchers;

namespace Integration.TradeXpress.Confirmations;

/// <summary>
/// <b>Teyit</b> — organizasyon-içi (kasa↔kasa) bir process'in <b>karşılıklı ayna onayı</b>. company-scoped,
/// per-tenant. Karşı taraf bir iç kasa olduğunda mevcut transaction paneli aynen çalışır ama sonuç HEMEN
/// postlanmaz: bir Teyit kaydı (Proposed) doğar. Alıcı, KENDİ ELİYLE kendi girişini oluşturur
/// (<see cref="Declare"/> — sistem aynalamaz), gönderen teyit eder (<see cref="Confirm"/>) → ancak o an iki
/// ayna bacak (gönderen −, alıcı +) atomik postlanır.
///
/// <para><b>İKİ PAYLOAD:</b> her taraf KENDİ satırını yazar ve o satır kendi payload'unda yaşar
/// (<see cref="InitiatorPayloadJson"/> / <see cref="CounterpartyPayloadJson"/>) — serileştirilmiş tam
/// <c>VoucherLineDto</c>. Alıcının payload'u gönderenden TÜRETİLMEZ; iki bağımsız beyandır. Skaler alanlar
/// (emtia/varyant/miktar/tutar/birimler/yön) payload'un <b>denormalize AYNA ANAHTARI</b>dır — sorgu/grid ve
/// ayna karşılaştırması için (<see cref="ToMirrorKey"/>).</para>
///
/// <para><b>Zero-trust:</b> tek taraflı beyan ötekinin defterini kımıldatmaz; SİSTEM karşı kaydı otomatik
/// üretmez — her taraf kendi gerçeğini kendi eliyle yazar. Yalnız çift-teyitli ayna çift postlanır. Değer,
/// teyit kapanana dek gönderenin sorumluluğundadır. Ayna kriteri (<see cref="ConfirmationMirrorKey"/> — TAM
/// ayna: emtia/varyant/miktar/tutar/ana+karşılık birimi, ZIT yön) AppService'te doğrulanır — tutmazsa fark
/// ekrana düşer (fire/kayıp dedektörü). <b>İptal yoktur:</b> gönderen teklifi geri çekemez; süreci yalnız
/// alıcı reddederek durdurabilir.</para>
///
/// <para><b>Sorumluluk = kendi eliyle yazdığın kayıt:</b> alıcı kendi girişini kaydedince o girişin sorumluluğu
/// artık ALICININDIR — kendi eliyle yazdığı için sonradan inkâr edemez. Sistemin aynalamamasının asıl sebebi de
/// budur: kimse kimsenin kaydını onun yerine yazamaz, herkes kendi gerçeğini sahiplenir.</para>
///
/// <para>Aggregate sınırı: Company/Vault/CurrencyUnit ve gerçekleşen fişler (Initiator/CounterpartyVoucherId)
/// id-only referanstır (navigation YOK). Payload'lar opak taşınır; materyalizasyonda AppService yorumlar.
/// Postlama AppService'te orkestre edilir.</para>
/// </summary>
public class Confirmation : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected Confirmation()
    {
    }

    public Confirmation(
        Guid companyId,
        Guid initiatorVaultId,
        Guid counterpartyVaultId,
        ConfirmationMirrorKey key,
        string initiatorPayloadJson,
        string? note = null)
    {
        SetCompany(companyId);
        SetVaults(initiatorVaultId, counterpartyVaultId);
        SetMirrorKey(key);
        SetInitiatorPayload(initiatorPayloadJson);
        SetNote(note);
        Status = ConfirmationStatus.Proposed;
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — id-only referans (company-scoped). Oluşturmadan sonra değişmez.</summary>
    public virtual Guid CompanyId { get; protected set; }

    /// <summary>Başlatan (gönderen) kasa — id-only referans. Oluşturmadan sonra değişmez.</summary>
    public virtual Guid InitiatorVaultId { get; protected set; }

    /// <summary>Karşı iç kasa (alıcı) — id-only referans. Oluşturmadan sonra değişmez.</summary>
    public virtual Guid CounterpartyVaultId { get; protected set; }

    // ── Ayna anahtarı: payload'un denormalize ölçütleri (spec §3) — ToMirrorKey ile okunur ──

    /// <summary>Hangi transaction paneli ürettiyse o process tipi (materyalizasyon + gösterim).</summary>
    public virtual ProcessType ProcessType { get; protected set; }

    /// <summary>Gönderenin yönü; aynası AYNI EKSENDE zıt yöndür (Giriş↔Çıkış, Alış↔Satış…).</summary>
    public virtual ProcessDirectionType Direction { get; protected set; }

    /// <summary>Ayna kriteri emtiası (Nakit/Maden/Mamül…) — id-only; emtiasız tiplerde null.</summary>
    public virtual Guid? CommodityId { get; protected set; }

    /// <summary>Ayna kriteri varyantı (çok-varyantlı emtiada) — id-only; varyantsızda null.</summary>
    public virtual Guid? VariantId { get; protected set; }

    /// <summary>Ayna kriteri miktarı (adet/gram). Tipe göre 0 olabilir (ör. Nakit).</summary>
    public virtual decimal Quantity { get; protected set; }

    /// <summary>Ayna kriteri tutarı. Tipe göre 0 olabilir (ör. yalnız-adet satırı).</summary>
    public virtual decimal Amount { get; protected set; }

    /// <summary>Ayna kriteri ANA birimi (<c>VoucherLine.MainUnitId</c> karşılığı) — id-only referans.
    /// <b>Null olabilir:</b> ana bacağı olmayan tiplerde (Dekont) boştur — değer karşılık bacağındadır.</summary>
    public virtual Guid? MainUnitId { get; protected set; }

    /// <summary>Ayna kriteri KARŞILIK birimi (<c>VoucherLine.PayUnitId</c>) — id-only; karşılıksızda null.</summary>
    public virtual Guid? PayUnitId { get; protected set; }

    /// <summary>Ayna kriteri karşılık tutarı (<c>VoucherLine.PayTotal</c>). Değerlemesiz teslimde 0.</summary>
    public virtual decimal PayTotal { get; protected set; }

    // ── Payload'lar: her taraf KENDİ satırını yazar (sistem aynalamaz) ──

    /// <summary>BAŞLATANIN kendi eliyle yazdığı satır — serileştirilmiş <c>VoucherLineDto</c>; teyitte replay
    /// edilir. Domain opak taşır (yorumlamaz). Teklifte ZORUNLU.</summary>
    public virtual string InitiatorPayloadJson { get; protected set; } = string.Empty;

    /// <summary>ALICININ kendi eliyle yazdığı satır — serileştirilmiş <c>VoucherLineDto</c>. <see cref="Declare"/>'e
    /// kadar null (sistem AYNALAMAZ; gönderenin payload'undan TÜRETİLMEZ).</summary>
    public virtual string? CounterpartyPayloadJson { get; protected set; }

    /// <summary>Teyit durumu.</summary>
    public virtual ConfirmationStatus Status { get; protected set; }

    /// <summary>Gönderenin açıklaması (opsiyonel).</summary>
    public virtual string? Note { get; protected set; }

    /// <summary>Alıcının karar açıklaması — red gerekçesi / teyit notu (opsiyonel).</summary>
    public virtual string? DecisionNote { get; protected set; }

    /// <summary>Gönderen bacağını gerçekleyen fiş — <see cref="Confirm"/>'de iliştirilir (id-only, nullable).</summary>
    public virtual Guid? InitiatorVoucherId { get; protected set; }

    /// <summary>Alıcı bacağını gerçekleyen fiş — <see cref="Confirm"/>'de iliştirilir (id-only, nullable).</summary>
    public virtual Guid? CounterpartyVoucherId { get; protected set; }

    #endregion

    #region Methods

    /// <summary>Bu Teyit'in (başlatan bacağının) ayna anahtarı — alıcının beyanı bununla kıyaslanır.
    /// Skaler alanlardan kurulur. EF'e complex-type olarak sızmasın diye property DEĞİL metot.</summary>
    public ConfirmationMirrorKey ToMirrorKey()
    {
        return new ConfirmationMirrorKey(
            ProcessType,
            Direction,
            CommodityId,
            VariantId,
            Quantity,
            Amount,
            MainUnitId,
            PayUnitId,
            PayTotal);
    }

    /// <summary>Teklif → Beyan edildi. Alıcı KENDİ ELİYLE kendi girişini oluşturdu (sistem aynalamaz) ve o satır
    /// payload olarak saklanır; gönderenin teyidi beklenir. Postlama YOK. Alıcının kaydının gönderenin kaydıyla
    /// AYNA olduğu (<see cref="ConfirmationMirrorKey"/>) AppService'te doğrulanmış olmalı — tutmazsa buraya
    /// gelinmez.</summary>
    public virtual void Declare(string counterpartyPayloadJson, string? note = null)
    {
        EnsureStatus(ConfirmationStatus.Proposed);
        SetCounterpartyPayload(counterpartyPayloadJson);
        SetDecisionNote(note);
        Status = ConfirmationStatus.Declared;
    }

    /// <summary>Beyan edildi → Teyitlendi. Gönderen alıcının aynasını teyit eder; HER İKİ bacak (gönderen −,
    /// alıcı +) atomik postlandıktan sonra çağrılır, gerçekleşen iki fiş iliştirilir.</summary>
    public virtual void Confirm(Guid initiatorVoucherId, Guid counterpartyVoucherId, string? note = null)
    {
        EnsureStatus(ConfirmationStatus.Declared);
        SetInitiatorVoucher(initiatorVoucherId);
        SetCounterpartyVoucher(counterpartyVoucherId);
        SetDecisionNote(note);
        Status = ConfirmationStatus.Confirmed;
    }

    /// <summary>Teklif|Beyan → Reddedildi. Alıcı kabul etmez; postlanmış bacak olmadığından yalnız durum kapanır.</summary>
    public virtual void Reject(string? reason)
    {
        if (Status != ConfirmationStatus.Proposed && Status != ConfirmationStatus.Declared)
        {
            throw new BusinessException("TradeXpress:Confirmation:InvalidStateTransition")
                .WithData("current", Status)
                .WithData("expected", $"{ConfirmationStatus.Proposed}|{ConfirmationStatus.Declared}");
        }

        SetDecisionNote(reason);
        Status = ConfirmationStatus.Rejected;
    }

    public virtual void SetNote(string? note)
    {
        Note = StringFieldGuard.EnsureOptionalText(
            note, nameof(Note), 0, ConfirmationConsts.NoteMaxLength);
    }

    public override string ToString()
    {
        return $"{base.ToString()}, {ProcessType} {Direction} {Quantity}/{Amount} [{Status}]";
    }

    private void SetMirrorKey(ConfirmationMirrorKey key)
    {
        if (key == null)
        {
            throw new RequiredPropertyException(nameof(ConfirmationMirrorKey));
        }

        EnsureValuesDeclared(key.Quantity, key.Amount, key.PayTotal);

        ProcessType = key.Type;
        Direction   = key.Direction;
        CommodityId = key.CommodityId;
        VariantId   = key.VariantId;
        Quantity    = key.Quantity;
        Amount      = key.Amount;
        MainUnitId  = key.MainUnitId;
        PayUnitId   = key.PayUnitId;
        PayTotal    = key.PayTotal;
    }

    /// <summary>Değer guard'ı: negatif YASAK + Miktar/Tutar/Karşılık'tan EN AZ BİRİ &gt; 0.
    /// <para>Tek bir "Amount &gt; 0" kuralı YETMEZ (2026-07-14 kararı): tipe göre hangi alanın taşıdığı değişir —
    /// Nakit'te Quantity=0 (değer tutarda), Mamül/Taş'ta yalnız-adet satırında Amount=0, <b>Dekont'ta ise ana
    /// bacak tümüyle boştur</b> (Miktar=Tutar=0; değer yalnız karşılık bacağında/PayTotal). Kuralın ASIL amacı
    /// "ortada teyide konu bir değer olsun" — üçü birden 0 ise değer yoktur.</para></summary>
    private static void EnsureValuesDeclared(decimal quantity, decimal amount, decimal payTotal)
    {
        if (quantity < 0m || amount < 0m || payTotal < 0m)
        {
            throw new BusinessException("TradeXpress:Confirmation:NegativeValue")
                .WithData("quantity", quantity)
                .WithData("amount", amount)
                .WithData("payTotal", payTotal);
        }

        if (quantity <= 0m && amount <= 0m && payTotal <= 0m)
        {
            throw new BusinessException("TradeXpress:Confirmation:ValueRequired");
        }
    }

    private void SetInitiatorPayload(string payloadJson)
    {
        InitiatorPayloadJson = StringFieldGuard.EnsureRequiredText(
            payloadJson, nameof(InitiatorPayloadJson), 1, ConfirmationConsts.PayloadMaxLength);
    }

    private void SetCounterpartyPayload(string payloadJson)
    {
        CounterpartyPayloadJson = StringFieldGuard.EnsureRequiredText(
            payloadJson, nameof(CounterpartyPayloadJson), 1, ConfirmationConsts.PayloadMaxLength);
    }

    private void SetDecisionNote(string? note)
    {
        DecisionNote = StringFieldGuard.EnsureOptionalText(
            note, nameof(DecisionNote), 0, ConfirmationConsts.DecisionNoteMaxLength);
    }

    private void EnsureStatus(ConfirmationStatus expected)
    {
        if (Status != expected)
        {
            throw new BusinessException("TradeXpress:Confirmation:InvalidStateTransition")
                .WithData("current", Status)
                .WithData("expected", expected);
        }
    }

    private void SetVaults(Guid initiatorVaultId, Guid counterpartyVaultId)
    {
        if (initiatorVaultId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(InitiatorVaultId));
        }

        if (counterpartyVaultId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(CounterpartyVaultId));
        }

        if (initiatorVaultId == counterpartyVaultId)
        {
            throw new BusinessException("TradeXpress:Confirmation:SameVault");
        }

        InitiatorVaultId    = initiatorVaultId;
        CounterpartyVaultId = counterpartyVaultId;
    }

    private void SetInitiatorVoucher(Guid initiatorVoucherId)
    {
        if (initiatorVoucherId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(InitiatorVoucherId));
        }

        InitiatorVoucherId = initiatorVoucherId;
    }

    private void SetCounterpartyVoucher(Guid counterpartyVoucherId)
    {
        if (counterpartyVoucherId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(CounterpartyVoucherId));
        }

        CounterpartyVoucherId = counterpartyVoucherId;
    }

    // Company set-once (oluşturmada) → public mutator YOK; yalnız ctor.
    private void SetCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(CompanyId));
        }

        CompanyId = companyId;
    }

    #endregion
}
