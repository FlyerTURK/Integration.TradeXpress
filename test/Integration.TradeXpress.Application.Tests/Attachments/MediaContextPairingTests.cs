using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Commodities;
using Integration.TradeXpress.Variants;
using Shouldly;
using Volo.Abp.Modularity;
using Xunit;

namespace Integration.TradeXpress.Attachments;

/// <summary>
/// CLAUDE.md §6 <b>"HER MEDYA TİPİ İKİ BAĞLAMI DA TAŞIR"</b> kuralının mekanik ağı.
///
/// <para><b>Neden gerekli:</b> bir bağlamı bağlayıp diğerini unutmak istisna FIRLATMAZ — medya sessizce "yok"
/// görünür. Sapma kimsenin bakmadığı yerde büyür: canlıda 185 medya bağının tamamı <c>Product</c> bağlamındaydı,
/// <c>ProductVariant</c>'ta sıfır; Good'da ise ne DTO alanı ne panel vardı. 2026-08-07'de aynı boşluğun
/// Jewelry/Metal/Stone'da da olduğu ölçüldü: <c>CommodityAgnosticGraph</c> yalnız VARYANT medyasını yazıyordu,
/// kayıt-geneli medya hiçbir zaman kaydedilmiyordu. Doküman kuralı unutulur; test kırmızı yanar.</para>
/// </summary>
public abstract class MediaContextPairingTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly CommodityAgnosticGraph _graph;
    private readonly IEntityMediaAppService _entityMedia;
    private readonly IMediaAppService _mediaService;

    protected MediaContextPairingTests()
    {
        _graph = GetRequiredService<CommodityAgnosticGraph>();
        _entityMedia = GetRequiredService<IEntityMediaAppService>();
        _mediaService = GetRequiredService<IMediaAppService>();
    }

    /// <summary>Kayıt listesi BOŞ kalmamalı ve her çiftin iki kolu da dolu + BİRBİRİNDEN FARKLI olmalı.
    /// Aynı dizeyi iki kola da yazmak kayıt ve varyant medyasını tek havuza çökertirdi.</summary>
    [Fact]
    public void Every_registered_media_type_declares_two_distinct_contexts()
    {
        MediaEntityNames.Registered.ShouldNotBeEmpty();

        foreach (var pair in MediaEntityNames.Registered)
        {
            pair.Record.ShouldNotBeNullOrWhiteSpace();
            pair.Variant.ShouldNotBeNullOrWhiteSpace();
            pair.Variant.ShouldNotBe(pair.Record, $"{pair.Record}: iki bağlam AYNI dizeyi taşıyamaz.");
        }

        MediaEntityNames.Registered.Select(p => p.Record).ShouldBeUnique();
        MediaEntityNames.Registered.Select(p => p.Variant).ShouldBeUnique();
    }

    /// <summary>Paylaşılan emtia grafı (Jewelry · Metal · Stone bunu kullanır) İKİ bağlamı da YAZMALI ve
    /// OKUMALI. Bu testin çivilediği tam olarak 2026-08-07'de bulunan boşluktur: kayıt-geneli medya
    /// <c>SaveAsync</c>'te hiç yazılmıyor, <c>LoadAsync</c>'te hiç okunmuyordu.</summary>
    [Fact]
    public async Task Commodity_graph_round_trips_both_record_and_variant_media()
    {
        var entityId = Guid.NewGuid();
        var recordMedia = await UploadAsync("kayit-geneli.png", TransparentPixelPng);
        var variantMedia = await UploadAsync("varyant-farki.png", RedPixelPng);
        variantMedia.ShouldNotBe(recordMedia, "İki görsel FARKLI olmalı; aksi halde dedup tek kayda indirir.");

        var variant = new EntityVariantGraphDto
        {
            Name = "Varyant A",
            Code = "VAR-A",
            IsMain = true,
            IsActive = true,
            Media = new List<EntityMediaLinkEditDto> { LinkTo(variantMedia) },
        };

        await WithUnitOfWorkAsync(async () =>
        {
            await _graph.SaveAsync(
                MediaEntityNames.Jewelry, MediaEntityNames.JewelryVariant, entityId, companyId: null, ownerName: "Test",
                documents: new List<EntityDocumentEditDto>(),
                notes: new List<EntityNoteEditDto>(),
                attributes: new List<EntityAttributeGraphDto>(),
                variants: new List<EntityVariantGraphDto> { variant },
                media: new List<EntityMediaLinkEditDto> { LinkTo(recordMedia) });
            return true;
        });

        var loaded = await WithUnitOfWorkAsync(async () =>
            await _graph.LoadAsync(MediaEntityNames.Jewelry, MediaEntityNames.JewelryVariant, entityId));

        // 1) KAYIT geneli: yazıldı ve geri okundu (eski hâlde bu liste HEP boştu).
        loaded.Media.ShouldHaveSingleItem().MediaId.ShouldBe(recordMedia);

        // 2) VARYANT farkı: ayrı bağlamda, kayıt medyasıyla KARIŞMADAN durdu.
        var loadedVariant = loaded.Variants.ShouldHaveSingleItem();
        loadedVariant.Media.ShouldHaveSingleItem().MediaId.ShouldBe(variantMedia);

        // 3) İki bağlam AYRI depodur — birinin içeriği diğerinde görünmez.
        var recordLinks = await WithUnitOfWorkAsync(async () =>
            await _entityMedia.GetForAsync(MediaEntityNames.Jewelry, entityId));
        recordLinks.ShouldNotContain(l => l.MediaId == variantMedia);
    }

    /// <summary>Silme İKİ bağlamı da temizlemeli — kayıt-geneli bağ geride kalırsa yetim link kalır ve
    /// kütüphanede silinmiş kaydın görselleri "kullanımda" görünmeye devam ederdi.</summary>
    [Fact]
    public async Task Commodity_graph_delete_clears_record_level_media_too()
    {
        var entityId = Guid.NewGuid();
        var recordMedia = await UploadAsync("silinecek.png", TransparentPixelPng);

        await WithUnitOfWorkAsync(async () =>
        {
            await _graph.SaveAsync(
                MediaEntityNames.Stone, MediaEntityNames.StoneVariant, entityId, companyId: null, ownerName: "Test",
                documents: new List<EntityDocumentEditDto>(),
                notes: new List<EntityNoteEditDto>(),
                attributes: new List<EntityAttributeGraphDto>(),
                variants: new List<EntityVariantGraphDto>(),
                media: new List<EntityMediaLinkEditDto> { LinkTo(recordMedia) });
            return true;
        });

        await WithUnitOfWorkAsync(async () =>
        {
            await _graph.DeleteAsync(MediaEntityNames.Stone, MediaEntityNames.StoneVariant, entityId);
            return true;
        });

        (await WithUnitOfWorkAsync(async () =>
            await _entityMedia.GetForAsync(MediaEntityNames.Stone, entityId))).ShouldBeEmpty();
    }

    // ⚠ İKİ FARKLI görsel şart: yükleme pipeline'ı içerik-hash'iyle DEDUP eder (CLAUDE.md §6) — aynı baytları
    // iki kez yüklemek TEK Media kaydı üretir ve "kayıt medyası ≠ varyant medyası" iddiası anlamsızlaşır.
    // (Bu testin ilk hâli tam buna düştü; kod değil test yanlıştı.) İkisi de geçerli 1×1 PNG, farklı piksel.
    private const string TransparentPixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    private const string RedPixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private async Task<Guid> UploadAsync(string fileName, string base64Png)
    {
        var dto = await WithUnitOfWorkAsync(async () =>
            await _mediaService.UploadAsync(new MediaUploadDto
            {
                FileName = fileName,
                Content = Convert.FromBase64String(base64Png),
            }));

        return dto.Id;
    }

    private static EntityMediaLinkEditDto LinkTo(Guid mediaId)
    {
        return new EntityMediaLinkEditDto
        {
            MediaId = mediaId,
            IsActive = true,
            IsDefault = true,
            DisplayOrder = 0,
        };
    }
}
