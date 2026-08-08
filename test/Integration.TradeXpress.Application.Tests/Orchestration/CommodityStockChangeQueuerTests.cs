using System;
using System.Linq;
using Integration.TradeXpress.Vouchers;
using Shouldly;
using Volo.Abp.Guids;
using Xunit;

namespace Integration.TradeXpress.Orchestration;

/// <summary>
/// EMTİA STOK TETİĞİNİN ANAHTAR TOPLAYICISI — <see cref="CommodityStockChangeQueuer.CollectKeys"/>.
///
/// <para>Bu iskelet üç sınıfa birebir kopyalanmıştı ve DÖRDÜNCÜSÜ (rezervasyonun serbest bırakılması) hiç
/// yoktu. Kopyaların birleştirilmesi davranış-koruyan bir refactor'dı; buradaki testler o davranışın
/// sözleşmesini yazıya döküyor.</para>
///
/// <para><b>En kritik test <c>Soft_deleted_lines_are_not_collected</c>:</b> "önce" kümesinin neden gerekli
/// olduğunun kanıtı. Satır soft-delete edildikten SONRA toplamak BOŞ küme verir — yani serbest bırakma
/// yolunda anahtarları silmeden önce almazsak hiçbir emtia için olay yayımlanmaz ve stok kanalda bayat
/// kalır. Hata sessizdir: istisna yok, log yok, yalnız geç kalan bir sayı.</para>
/// </summary>
public class CommodityStockChangeQueuerTests
{
    [Fact]
    public void Collects_one_key_per_tracked_commodity()
    {
        var voucher = NewVoucher();
        AddLine(voucher, ProcessType.Metal, commodityId: MetalId);
        AddLine(voucher, ProcessType.Good, commodityId: GoodId);

        var keys = CommodityStockChangeQueuer.CollectKeys(voucher);

        keys.Count.ShouldBe(2);
        keys.ShouldContain(k => k.Family == ProcessType.Metal && k.CommodityId == MetalId);
        keys.ShouldContain(k => k.Family == ProcessType.Good && k.CommodityId == GoodId);
    }

    /// <summary>SOFT-DELETE edilmiş satır toplanmaz — "önce" kümesinin varlık sebebi.
    /// <para>Serbest bırakma satırları soft-delete eder; anahtarlar SİLMEDEN ÖNCE alınmazsa toplayıcı boş
    /// döner ve stok tetiği hiç doğmaz.</para></summary>
    [Fact]
    public void Soft_deleted_lines_are_not_collected()
    {
        var voucher = NewVoucher();
        var line = AddLine(voucher, ProcessType.Metal, commodityId: MetalId);

        CommodityStockChangeQueuer.CollectKeys(voucher).ShouldHaveSingleItem();

        voucher.RemoveLine(line.Id);

        CommodityStockChangeQueuer.CollectKeys(voucher).ShouldBeEmpty();
    }

    /// <summary>Stok TAŞIMAYAN aile kapsam dışıdır — Nakit satırı stok tetiği doğurmaz.</summary>
    [Fact]
    public void Untracked_families_are_excluded()
    {
        var voucher = NewVoucher();
        AddLine(voucher, ProcessType.Cash, commodityId: MetalId);

        CommodityStockChangeQueuer.CollectKeys(voucher).ShouldBeEmpty();
    }

    /// <summary>Aynı emtianın birden çok satırı TEK anahtara iner (gereksiz push tetiklenmez).</summary>
    [Fact]
    public void Duplicate_commodity_lines_collapse_to_one_key()
    {
        var voucher = NewVoucher();
        AddLine(voucher, ProcessType.Metal, commodityId: MetalId);
        AddLine(voucher, ProcessType.Metal, commodityId: MetalId);

        CommodityStockChangeQueuer.CollectKeys(voucher).ShouldHaveSingleItem();
    }

    /// <summary>AİLE anahtarın parçasıdır: aynı Guid farklı ailede AYRI anahtardır.
    /// <para><c>CommodityId</c> FK'sız bir snapshot'tır; aile anahtara girmezse "3 gram maden" ile "3 adet
    /// mamül" aynı kayıt sanılırdı.</para></summary>
    [Fact]
    public void Family_is_part_of_the_key()
    {
        var voucher = NewVoucher();
        var shared = SimpleGuidGenerator.Instance.Create();
        AddLine(voucher, ProcessType.Metal, commodityId: shared);
        AddLine(voucher, ProcessType.Good, commodityId: shared);

        CommodityStockChangeQueuer.CollectKeys(voucher).Count.ShouldBe(2);
    }

    /// <summary>Emtiası ÇÖZÜLMEMİŞ satır (CommodityId null) anahtar üretmez — uydurma id ile push tetiklenmez.</summary>
    [Fact]
    public void Lines_without_a_commodity_id_are_skipped()
    {
        var voucher = NewVoucher();
        AddLine(voucher, ProcessType.Metal, commodityId: null);

        CommodityStockChangeQueuer.CollectKeys(voucher).ShouldBeEmpty();
    }

    // ── fixture ──────────────────────────────────────────────────────────────────────────────────────

    private static readonly Guid MetalId = SimpleGuidGenerator.Instance.Create();
    private static readonly Guid GoodId = SimpleGuidGenerator.Instance.Create();

    private static Voucher NewVoucher()
    {
        return new Voucher(
            SimpleGuidGenerator.Instance.Create(),
            SimpleGuidGenerator.Instance.Create(),
            SimpleGuidGenerator.Instance.Create(),
            AccountType.CurrentAccount,
            SimpleGuidGenerator.Instance.Create(),
            "ACC",
            SimpleGuidGenerator.Instance.Create(),
            "SUB",
            voucherNumber: 1,
            voucherDate: new DateTime(2026, 8, 7));
    }

    private static VoucherLine AddLine(Voucher voucher, ProcessType type, Guid? commodityId)
    {
        return voucher.AddLine(SimpleGuidGenerator.Instance.Create(), VoucherLineDtoFactory.ToLineInput(
            new VoucherLineDto
            {
                BranchId    = voucher.BranchId,
                VaultId     = voucher.VaultId,
                AccountId   = voucher.AccountId,
                SubAccountId = voucher.SubAccountId,
                Type        = type,
                Direction   = ProcessDirectionType.Outbound,
                PaymentType = ProcessPaymentType.Normal,
                CommodityId = commodityId,
                Quantity    = 1m,
                Amount      = 1m,
                Factor      = 1m,
                Total       = 1m,
            }));
    }
}
