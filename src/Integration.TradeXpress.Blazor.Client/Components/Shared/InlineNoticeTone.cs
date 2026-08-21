namespace Integration.TradeXpress.Blazor.Client.Components.Shared;

/// <summary>
/// Satır içi bildirim kutusunun (<see cref="InlineWarning"/>) TONU — "ne kadar acil" bilgisini renge çevirir.
///
/// <para><b>Neden nötr bir enum:</b> kutu artık iki farklı ağırlığı taşıyor (satışa-hazırlık bandı engel
/// taşıyorsa kırmızı, yalnız uyarı taşıyorsa amber). Parametreyi doğrudan <c>SaleReadinessSeverity</c> ile
/// yazmak, projedeki HER satır-içi uyarıyı ürün-satış alanına bağlardı; kutu ise Margin/Bilanço gibi ilgisiz
/// ekranlarda da kullanılıyor. Ton nötr kalır, eşleme çağıranın işidir.</para>
/// </summary>
public enum InlineNoticeTone
{
    /// <summary>Amber — engellemeyen uyarı (kutunun ÖNCEKİ ve varsayılan görünümü; mevcut kullanımlar değişmez).</summary>
    Warning = 0,

    /// <summary>Kırmızı — engelleyici issue (iş bu hâliyle ilerleyemez).</summary>
    Danger = 1,
}
