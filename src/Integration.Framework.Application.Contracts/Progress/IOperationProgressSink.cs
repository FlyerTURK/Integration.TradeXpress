using System;

namespace Integration.Framework.Progress;

/// <summary>Uzun süren bir işlemin ilerleme anlık görüntüsü — faz + sayaç + o anki kalem.
/// <para><see cref="Total"/> null = toplam henüz bilinmiyor (belirsiz/akan faz, ör. sayfa sayfa çekim);
/// dolu = <see cref="Current"/>/<see cref="Total"/> oranı çizilebilir.</para></summary>
public sealed record OperationProgress(
    string Phase,
    int Current,
    int? Total,
    string? Message = null)
{
    public double? Percent
    {
        get
        {
            if (Total is not > 0)
            {
                return null;
            }

            return Math.Min(100d, Current * 100d / Total.Value);
        }
    }
}

/// <summary>
/// UZUN İŞLEM İLERLEME KANALI — app service yazar, UI okur. Blazor Server'da app service ile bileşen aynı
/// circuit/scope'ta olduğundan ayrı hub/SignalR gerekmez: bileşen bu scoped servise abone olur, servis her
/// <see cref="Report"/>'ta olayı yükseltir, bileşen <c>InvokeAsync(StateHasChanged)</c> ile çizer.
///
/// <para><b>Neden ambient (parametre değil):</b> raporlayan metotların imzası (ör. <c>ImportFromMarketplaceAsync</c>)
/// HTTP API'den de çağrılır; imzaya bir sink parametresi eklemek sözleşmeyi UI'a bağlardı. Ambient servis varsa
/// yazar, dinleyen yoksa yazdığı kaybolur — sessiz ve zararsız (ayrı bir "null sink" yerine tek gerçek
/// uygulama + dinleyicisi olmayan olay).</para>
///
/// <para><b>Sözleşme:</b> raporlama İSTEĞE BAĞLI ipucudur — işin doğruluğunu etkilemez, hata fırlatmaz.</para>
/// </summary>
public interface IOperationProgressSink
{
    /// <summary>İlerleme değişti — UI aboneleri için. Raporlayan bekletilmez.</summary>
    event Action<OperationProgress>? Reported;

    /// <summary>Son raporlanan durum (abone geç bağlansa da mevcut hâli görsün).</summary>
    OperationProgress? Current { get; }

    void Report(OperationProgress progress);

    /// <summary>İşlem bitti — son durumu temizler (panel kapanır).</summary>
    void Complete();
}
