using System.Text.Json;
using System.Text.Json.Serialization;
using Integration.TradeXpress.Vouchers;
using Volo.Abp;

namespace Integration.TradeXpress.Confirmations;

/// <summary>
/// Teyit payload'unun (bir tarafın KENDİ eliyle yazdığı <see cref="VoucherLineDto"/>) serileştirme kapısı.
/// Teklif/beyanda yazılır, teyitte replay için okunur.
///
/// <para><b>Neden dar seçenekler:</b> payload DB kolonuna sığmalı (<see cref="ConfirmationConsts.PayloadMaxLength"/>).
/// <c>VoucherLineDto</c> ~70 alanlıdır ama bir satırda çoğu default'tur (Takoz alanları vb.) →
/// <c>WhenWritingDefault</c> ile yalnız DOLU alanlar yazılır; tipik satır birkaç yüz karakter kalır.</para>
///
/// <para><b>Kapsam dışı alanlar okumada temizlenir:</b> id/fiş başlığı/denormalize kodlar ve yürüyen bakiye
/// payload'da anlamsızdır — teyitte fiş başlığını SUNUCU türetir (karşı kasanın vault-cari'si), satır id'si
/// yeni fişte yeniden doğar. Bunları taşımak bayat veri sızdırır.</para>
/// </summary>
public static class ConfirmationPayloadSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Satırı payload'a çevirir. Kolon sınırını AŞARSA sessizce kırpmaz — fail-fast.</summary>
    public static string Serialize(VoucherLineDto line)
    {
        var json = JsonSerializer.Serialize(Sanitize(line), Options);
        if (json.Length > ConfirmationConsts.PayloadMaxLength)
        {
            throw new BusinessException("TradeXpress:Confirmation:PayloadTooLarge")
                .WithData("length", json.Length)
                .WithData("max", ConfirmationConsts.PayloadMaxLength);
        }

        return json;
    }

    /// <summary>Payload'ı satıra çevirir. Bozuk/boş payload = veri bütünlüğü hatası (teyit postlanamaz).</summary>
    public static VoucherLineDto Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new BusinessException("TradeXpress:Confirmation:PayloadMissing");
        }

        VoucherLineDto? line;
        try
        {
            line = JsonSerializer.Deserialize<VoucherLineDto>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new BusinessException("TradeXpress:Confirmation:PayloadCorrupt", ex.Message);
        }

        return line ?? throw new BusinessException("TradeXpress:Confirmation:PayloadCorrupt");
    }

    /// <summary>Payload'a girmeyecek alanları temizler: kimlik + fiş başlığı (sunucu türetir) + okuma-zamanı
    /// denormalize kodlar/bakiyeler + virman alanları (Virman iç kipte KAPALI — sızan değere güvenilmez).</summary>
    private static VoucherLineDto Sanitize(VoucherLineDto line)
    {
        line.Id                      = default;
        line.VoucherId               = null;
        line.VoucherNumber           = default;
        line.VoucherConcurrencyStamp = null;

        // Fiş başlığı: teyitte SUNUCU türetir (kasa + karşı kasanın vault-cari'si) → payload'da taşınmaz.
        line.CompanyId    = default;
        line.BranchId     = default;
        line.VaultId      = null;
        line.AccountId    = default;
        line.SubAccountId = null;

        // Okuma-zamanı türetilenler (DB'de saklanmaz; replay'de yeniden çözülür).
        line.MainUnitCode       = null;
        line.PayUnitCode        = null;
        line.CounterAccountCode = null;
        line.CreatorName        = null;
        line.CreatorId          = null;
        line.CreationTime       = default;
        line.RunningBalances    = new();

        // Virman'a özel alanlar — iç kipte Virman desteklenmez (ConfirmationProcessPolicy).
        line.CounterAccountId = null;
        line.LinkId           = null;

        return line;
    }
}
