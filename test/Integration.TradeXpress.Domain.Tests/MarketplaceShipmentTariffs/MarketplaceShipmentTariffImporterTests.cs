using System.Linq;
using Integration.TradeXpress.SalesChannels;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.MarketplaceShipmentTariffs;

/// <summary>
/// <see cref="MarketplaceShipmentTariffImporter"/> saf parse testleri (DB'siz) + gömülü N11 tarifesinin
/// bütünlük pini.
///
/// <para><b>Neden bu kadar sıkı:</b> kargo tarifesi doğrudan satış fiyatına giriyor. Eksik bir desi satırı
/// sessizce "o desi için fiyat yok" demek, yanlış ondalık ise 1000 kat sapma demek. Bu yüzden parser
/// FAIL-FAST'tir ve testler hem hata yollarını hem de gerçek seed verisinin kritik hücrelerini pinler.</para>
/// </summary>
public class MarketplaceShipmentTariffImporterTests
{
    private const string N11TariffFile = "n11-shipment-tariff-2026-07-26.tsv";

    private static string BuildMinimalTsv(string desiRows, string carrierRows = "PTT\tPTT Kargo\tPerPiece\t28.25\t0.30\t0.00")
    {
        return "[META]\nVersion\t2026-07-26\nChannel\tTrN11\nVatRate\t0.20\nPostalServiceFeeRate\t0.0235\n" +
               "[CARRIER]\nCode\tName\tChargeBasis\tOverflowIncrement\tFailedDeliveryRate\tExtraFee\n" +
               carrierRows + "\n" +
               "[DESI]\nDesi\tPTT\n" + desiRows + "\n";
    }

    /// <summary>0..100 arası TAM desi listesi (test kurgusu — gerçek fiyatlar değil).</summary>
    private static string FullDesiRows(int? skip = null)
    {
        return string.Join("\n", Enumerable
            .Range(0, MarketplaceShipmentTariffConsts.TabulatedMaxDesi + 1)
            .Where(d => d != skip)
            .Select(d => $"{d}\t{10 + d}.00"));
    }

    #region Gömülü N11 tarifesi — gerçek seed verisi

    [Fact]
    public void Embedded_n11_tariff_parses_without_errors()
    {
        var content = MarketplaceShipmentTariffImporter.ReadEmbeddedTsv(N11TariffFile);

        var result = MarketplaceShipmentTariffImporter.ParseTsv(content);

        result.Errors.ShouldBeEmpty();
        result.Channel.ShouldBe(SalesChannelType.TrN11);
        result.Version.ShouldBe("2026-07-26");
        result.VatRate.ShouldBe(0.20m);
        result.PostalServiceFeeRate.ShouldBe(0.0235m);
        result.ConditionalMaxDesi.ShouldBe(10);   // resmi metin: "10 desi ve altındaki gönderiler"
    }

    [Fact]
    public void Embedded_n11_tariff_has_all_six_carriers_with_full_desi_table()
    {
        var result = MarketplaceShipmentTariffImporter.ParseTsv(
            MarketplaceShipmentTariffImporter.ReadEmbeddedTsv(N11TariffFile));

        result.Carriers.Select(c => c.Code).ShouldBe(
            new[] { "ARAS", "SURAT", "PTT", "YURTICI", "KOLAYGELSIN", "DHL" }, ignoreOrder: true);

        foreach (var carrier in result.Carriers)
        {
            result.Rates[carrier.Code].Count.ShouldBe(
                MarketplaceShipmentTariffConsts.TabulatedMaxDesi + 1,
                $"{carrier.Code} desi tablosu 0..100 eksiksiz olmalı");
        }
    }

    /// <summary>PTT tek parça-başı taşıyıcı — resmi metin: "PTT Kargo firmasında hesaplama parça başı
    /// olarak yapılmaktadır." Diğerleri kümülatif; karıştırılırsa çok parçalı gönderi yanlış fiyatlanır.</summary>
    [Fact]
    public void Only_ptt_is_charged_per_piece()
    {
        var result = MarketplaceShipmentTariffImporter.ParseTsv(
            MarketplaceShipmentTariffImporter.ReadEmbeddedTsv(N11TariffFile));

        foreach (var carrier in result.Carriers)
        {
            var expected = carrier.Code == "PTT" ? ShipmentChargeBasis.PerPiece : ShipmentChargeBasis.Cumulative;
            carrier.ChargeBasis.ShouldBe(expected, carrier.Code);
        }
    }

    /// <summary>Kuyum bandının (desi 0-2) fiyatları — pratikte kullanılan tek aralık, kaynağa birebir sadık.</summary>
    [Fact]
    public void Jewelry_band_prices_match_the_published_tariff()
    {
        var result = MarketplaceShipmentTariffImporter.ParseTsv(
            MarketplaceShipmentTariffImporter.ReadEmbeddedTsv(N11TariffFile));

        result.Rates["PTT"][0].ShouldBe(75.76m);     // "Dosya" satırı
        result.Rates["PTT"][1].ShouldBe(75.76m);
        result.Rates["PTT"][2].ShouldBe(75.76m);
        result.Rates["ARAS"][0].ShouldBe(90.50m);
        result.Rates["SURAT"][0].ShouldBe(95.32m);
        result.Rates["YURTICI"][0].ShouldBe(117.84m);
        result.Rates["KOLAYGELSIN"][0].ShouldBe(98.39m);
        result.Rates["DHL"][0].ShouldBe(99.16m);
    }

    /// <summary>
    /// KAYNAK ANOMALİLERİ — n11'in yayınında GERÇEKTEN böyle (2026-07-26'da sayfanın HTML'i ile doğrulandı):
    /// PTT desi 99→100 arasında 1.233,30'dan 2.441,33'e sıçrıyor (diğer beş taşıyıcıda yok) ve desi 30→31'de
    /// hem PTT hem Sürat sıçrıyor. Bu test o hücreleri PİNLER: ileride biri "yazım hatası" sanıp sessizce
    /// düzeltirse kırmızı yansın. Düzeltme ancak n11 yayınını değiştirirse, YENİ yürürlük sürümü olarak gelir.
    /// </summary>
    [Fact]
    public void Source_anomalies_are_preserved_verbatim()
    {
        var result = MarketplaceShipmentTariffImporter.ParseTsv(
            MarketplaceShipmentTariffImporter.ReadEmbeddedTsv(N11TariffFile));

        result.Rates["PTT"][99].ShouldBe(1233.30m);
        result.Rates["PTT"][100].ShouldBe(2441.33m);

        result.Rates["PTT"][30].ShouldBe(286.04m);
        result.Rates["PTT"][31].ShouldBe(400.81m);
        result.Rates["SURAT"][30].ShouldBe(376.48m);
        result.Rates["SURAT"][31].ShouldBe(450.37m);
    }

    /// <summary>Yalnız Yurtiçi'nin SMS ücreti var (0,60 TL + KDV); başarısız teslimat oranları taşıyıcıya göre.</summary>
    [Fact]
    public void Carrier_surcharges_match_the_published_tariff()
    {
        var result = MarketplaceShipmentTariffImporter.ParseTsv(
            MarketplaceShipmentTariffImporter.ReadEmbeddedTsv(N11TariffFile));

        var byCode = result.Carriers.ToDictionary(c => c.Code);

        byCode["YURTICI"].ExtraFee.ShouldBe(0.60m);
        byCode.Values.Where(c => c.Code != "YURTICI").ShouldAllBe(c => c.ExtraFee == 0m);

        byCode["PTT"].FailedDeliveryRate.ShouldBe(0.30m);
        byCode["DHL"].FailedDeliveryRate.ShouldBe(1.00m);
        byCode["ARAS"].FailedDeliveryRate.ShouldBe(0.50m);
    }

    [Fact]
    public void Conditional_rates_cover_both_basket_tiers_for_every_carrier()
    {
        var result = MarketplaceShipmentTariffImporter.ParseTsv(
            MarketplaceShipmentTariffImporter.ReadEmbeddedTsv(N11TariffFile));

        result.ConditionalRates.Count.ShouldBe(12);   // 6 taşıyıcı × 2 dilim

        var pttLow = result.ConditionalRates.Single(r => r.CarrierCode == "PTT" && r.BasketFrom == 0m);
        pttLow.BasketTo.ShouldBe(149.99m);
        pttLow.Amount.ShouldBe(34.16m);

        var yurticiHigh = result.ConditionalRates.Single(r => r.CarrierCode == "YURTICI" && r.BasketFrom == 149.99m);
        yurticiHigh.Amount.ShouldBe(113.33m);
    }

    #endregion

    #region Fail-fast davranışı

    /// <summary>Tek bir desi satırı eksikse seed DURMALI — interpolasyonla uydurulmamalı.</summary>
    [Fact]
    public void Missing_desi_row_is_reported_not_interpolated()
    {
        var result = MarketplaceShipmentTariffImporter.ParseTsv(BuildMinimalTsv(FullDesiRows(skip: 42)));

        result.Errors.ShouldNotBeEmpty();
        result.Errors.ShouldContain(e => e.Contains("PTT") && e.Contains("42"));
        result.Rates["PTT"].ShouldNotContainKey(42);
    }

    [Fact]
    public void Unparseable_amount_is_reported()
    {
        var rows = FullDesiRows().Replace("5\t15.00", "5\tabc");

        var result = MarketplaceShipmentTariffImporter.ParseTsv(BuildMinimalTsv(rows));

        result.Errors.ShouldContain(e => e.Contains("abc"));
    }

    /// <summary>TR biçimli tutar ("1.031,56") REDDEDİLİR: virgül kaynakta hem ondalık hem binlik anlamına
    /// gelebiliyor — bu belirsizlik TSV üretiminde çözülür, parser'a taşınmaz.</summary>
    [Fact]
    public void Turkish_formatted_amount_is_rejected()
    {
        var rows = FullDesiRows().Replace("7\t17.00", "7\t1.031,56");

        var result = MarketplaceShipmentTariffImporter.ParseTsv(BuildMinimalTsv(rows));

        result.Errors.ShouldContain(e => e.Contains("1.031,56"));
    }

    [Fact]
    public void Duplicate_desi_row_is_reported()
    {
        var result = MarketplaceShipmentTariffImporter.ParseTsv(BuildMinimalTsv(FullDesiRows() + "\n7\t99.00"));

        result.Errors.ShouldContain(e => e.Contains("birden fazla"));
    }

    [Fact]
    public void Carrier_without_desi_column_is_reported()
    {
        var carriers = "PTT\tPTT Kargo\tPerPiece\t28.25\t0.30\t0.00\nARAS\tAras Kargo\tCumulative\t12.57\t0.50\t0.00";

        var result = MarketplaceShipmentTariffImporter.ParseTsv(BuildMinimalTsv(FullDesiRows(), carriers));

        result.Errors.ShouldContain(e => e.Contains("ARAS") && e.Contains("[DESI]"));
    }

    [Fact]
    public void Unknown_channel_is_reported()
    {
        var tsv = BuildMinimalTsv(FullDesiRows()).Replace("Channel\tTrN11", "Channel\tHepsiburada");

        var result = MarketplaceShipmentTariffImporter.ParseTsv(tsv);

        result.Errors.ShouldContain(e => e.Contains("Channel"));
    }

    [Fact]
    public void Comments_and_blank_lines_are_ignored()
    {
        var tsv = "# başlık yorumu\n\n" + BuildMinimalTsv(FullDesiRows());

        var result = MarketplaceShipmentTariffImporter.ParseTsv(tsv);

        result.Errors.ShouldBeEmpty();
    }

    #endregion
}
