using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Orders;

/// <summary>
/// İADE SÜRECİ SİNYALİ — <see cref="N11OrderStatusCatalog.IsReturnFlowSignal"/>.
///
/// <para><b>Neden ayrı pin:</b> iptal (51) ile iade (52/53 ve süreç kodları) İKİ FARKLI eksendir. Karışsalardı
/// iptal talebi "iade girişi bekliyor" diye görünür ve kullanıcı hiç çıkmamış bir malın iadesini kaydetmeye
/// çalışırdı; ya da iade talebi rezervasyonun iptal kararını uyandırıp teslim edilmiş malın stoğunu geri
/// verirdi. İkisi de sessiz stok bozulmasıdır.</para>
/// </summary>
public class ReturnFlowSignalTests
{
    [Theory]
    [InlineData("9")]    // İade Edildi
    [InlineData("11")]   // İade/İptal/Değişim Talep Edildi
    [InlineData("12")]   // Talep Tamamlandı
    [InlineData("13")]   // Kargoda İade
    [InlineData("16")]   // Teslim Edilmiş İade
    [InlineData("52")]   // İade Talep Edildi
    [InlineData("53")]   // Değişim Talep Edildi
    public void Return_flow_codes_are_recognised(string code)
    {
        N11OrderStatusCatalog.IsReturnFlowSignal(code).ShouldBeTrue();
    }

    /// <summary>⚠ 51 (İptal Talebi) iade DEĞİLDİR — o rezervasyonun iptal kararını uyandırır.</summary>
    [Fact]
    public void Cancellation_request_is_not_a_return_signal()
    {
        N11OrderStatusCatalog.IsReturnFlowSignal("51").ShouldBeFalse();
        N11OrderStatusCatalog.IsCancellationRequested("51").ShouldBeTrue();
    }

    /// <summary>İki eksen ÖRTÜŞMEZ — hiçbir kod ikisine birden ait olamaz.</summary>
    [Theory]
    [InlineData("9")]
    [InlineData("52")]
    [InlineData("53")]
    public void Return_codes_never_trigger_the_cancellation_bridge(string code)
    {
        N11OrderStatusCatalog.IsCancellationRequested(code).ShouldBeFalse();
    }

    [Theory]
    [InlineData("1")]     // Yeni
    [InlineData("7")]     // Teslim Edilmiş (normal akış)
    [InlineData(null)]
    [InlineData("abc")]
    public void Non_return_codes_are_rejected(string? code)
    {
        N11OrderStatusCatalog.IsReturnFlowSignal(code).ShouldBeFalse();
    }
}
