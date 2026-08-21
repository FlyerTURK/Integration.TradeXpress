using Integration.TradeXpress.Vouchers;

namespace Integration.TradeXpress.Confirmations;

/// <summary>
/// İç kasa (Teyit) kipinde hangi process tiplerinin kullanılabileceğinin <b>TEK KAYNAĞI</b> (SSOT).
/// UI (buton görünürlüğü) ve sunucu (Propose guard'ı) AYNI kuralı okur — client kendi listesini türetmez.
/// </summary>
public static class ConfirmationProcessPolicy
{
    /// <summary>Bu process tipi iç kasa kipinde (karşılıklı mirror onayı) kullanılabilir mi?
    ///
    /// <para><b>AÇIK</b> — iki tarafın da kendi eliyle simetrik satır yazabildiği tipler.</para>
    ///
    /// <para><b>KAPALI</b> (gerekçeler):
    /// <list type="bullet">
    ///   <item><b>Convert (Çevrim):</b> tek-taraflı birim dönüşümü — karşı taraf YOK, mirror'ın öznesi yok.</item>
    ///   <item><b>Assay (Çeşni):</b> karşı taraf DIŞ cari/ayar evi — iç kasa değil; kendi eliyle kayıt yazamaz.</item>
    ///   <item><b>Transfer (Virman):</b> cari→cari aktarım, kasa TAŞIMAZ → iç kasa mirror'ının öznesi yok.</item>
    ///   <item><b>Bullion (Takoz):</b> TAMAMEN kapalı. Her iç teslimin bir tarafı ÇIKIŞ olmak zorundadır; Takoz
    ///         çıkışı ise sunucu-otoriterdir (<c>VoucherBullionStockService.PrepareBullionExitLineAsync</c> metal
    ///         verisini seçilen GİRİŞ külçesinden kopyalar). O külçe alıcının kasasında bulunmadığından ne teklif
    ///         kurulabilir ne de mirror'ı doğrulanabilir → tip iç kipte fiilen kullanılamaz.</item>
    /// </list></para>
    /// </summary>
    public static bool IsInternalModeSupported(ProcessType type)
    {
        switch (type)
        {
            case ProcessType.Cash:
            case ProcessType.Metal:
            case ProcessType.Scrap:
            case ProcessType.Stone:
            case ProcessType.Jewelry:
            case ProcessType.Good:
            case ProcessType.Service:
            case ProcessType.Future:
            case ProcessType.DebitNote:
                return true;
            default:
                return false;
        }
    }
}
