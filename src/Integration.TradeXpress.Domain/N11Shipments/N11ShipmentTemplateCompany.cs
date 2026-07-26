using System;

namespace Integration.TradeXpress.N11Shipments;

/// <summary>
/// Şablonun KARGO FİRMASI satırı — hangi firmayla gönderiliyor (<see cref="ExternalId"/>, N11 aynasının kimliği)
/// ve o firmaya doğacak borcun yazılacağı varsayılan cari alt hesap (<see cref="SubAccountId"/>).
/// Owned tip (JSON kolonu; sorgulanmaz, N11'e yalnız kimlik push edilir) — repo deseni:
/// <c>SalesChannelEtsyProductListingAttribute</c>.
///
/// <para><b>Neden şablonun içinde</b> (2026-07-26 Hakan kararı): şablon zaten "bu kanalda şu firmalarla, şu
/// koşullarla gönderiyorum" demek; firmanın carisi de aynı cümlenin parçası. Şablon N11'den kalkarsa o şablonla
/// iş yapılmıyor demektir — bağın onunla pasifleşmesi doğaldır (bu yüzden senkron SİLMEZ, pasifleştirir).</para>
///
/// <para><b>Cari şirkete aittir, şablona değil:</b> aynı firma birden çok şablonda geçerse hepsi AYNI alt hesabı
/// göstermelidir — kargo firması hesap ekstresini şablon şablon ayırmaz, tek bakiye ister. Bu yüzden bağ
/// kurulduğunda kanaldaki TÜM şablonlara yayılır ve firma bir daha sorulmaz.</para>
///
/// <para><c>SubAccountId</c> <c>null</c> = ÖKSÜZ: henüz bir cariye bağlanmamış, kullanıcıya sorulacak.</para>
/// </summary>
public class N11ShipmentTemplateCompany
{
    #region Constructors

    protected N11ShipmentTemplateCompany()
    {
    }

    public N11ShipmentTemplateCompany(string externalId, Guid? subAccountId = null)
    {
        SetExternalId(externalId);
        SetSubAccount(subAccountId);
    }

    #endregion

    #region Properties

    /// <summary>N11 kargo firmasının kimliği (<c>N11ShipmentCompany.ExternalId</c>). Ad/kısa kod aynadan okunur —
    /// burada KOPYALANMAZ (tek kaynak ayna).</summary>
    public virtual string ExternalId { get; protected set; } = string.Empty;

    /// <summary>Varsayılan cari alt hesap (<c>SubAccount.Id</c>; id-only, nav YOK). <c>null</c> = öksüz.
    /// Kullanıcının KENDİ cari planından seçilir — sistem cari üretmez.</summary>
    public virtual Guid? SubAccountId { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetExternalId(string externalId)
    {
        ExternalId = StringFieldGuard.EnsureRequiredText(
            externalId, nameof(ExternalId), 1, N11ShipmentConsts.ExternalIdMaxLength);
    }

    /// <summary>Varsayılan cari alt hesabı bağlar/çözer (boş Guid → null = öksüz).</summary>
    public virtual void SetSubAccount(Guid? subAccountId)
    {
        SubAccountId = subAccountId == Guid.Empty ? null : subAccountId;
    }

    public override string ToString()
    {
        return ExternalId;
    }

    #endregion
}
