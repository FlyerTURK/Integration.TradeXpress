using System.Text.Json;
using System.Text.Json.Serialization;
using Volo.Abp;

namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// VoucherLineHistory anlık görüntüsünün (tam <see cref="VoucherLineDto"/>) serileştirme kapısı — Confirmation
/// payload'unun (<c>ConfirmationPayloadSerializer</c>) AYNI JSON seçenekleriyle (camelCase + yalnız dolu alan),
/// ama SANITIZE ETMEZ: tarihçe kaydı MİRROR/TAM kopyadır (id/fiş başlığı/denormalize kodlar dahil — popup
/// detayında hepsi gösterilir), Confirmation payload'u gibi replay girdisi DEĞİLDİR.
/// </summary>
public static class VoucherLineHistorySerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
    };

    public static string Serialize(VoucherLineDto line)
    {
        var json = JsonSerializer.Serialize(line, Options);
        if (json.Length > VoucherLineHistoryConsts.SnapshotMaxLength)
        {
            throw new BusinessException("TradeXpress:VoucherLineHistory:SnapshotTooLarge")
                .WithData("length", json.Length)
                .WithData("max", VoucherLineHistoryConsts.SnapshotMaxLength);
        }

        return json;
    }

    public static VoucherLineDto Deserialize(string json)
    {
        return JsonSerializer.Deserialize<VoucherLineDto>(json, Options)
               ?? throw new BusinessException("TradeXpress:VoucherLineHistory:SnapshotCorrupt");
    }
}
