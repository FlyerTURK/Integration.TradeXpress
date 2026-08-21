using System;
using System.Threading.Tasks;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Variants;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Xunit;

namespace Integration.TradeXpress.Orders;

/// <summary>
/// Sipariş satırı "eşleşme anı" görünümü — <c>OrderLineProductSnapshotBuilder.BuildAsync</c> sözleşmesi.
///
/// <para><b>Neden var:</b> satır belirli bir VARYANTA eşleşir; thumbnail'in varyantın kendi setinden
/// ("ProductVariant" bağlamı) gelmesi, yoksa ürünün kayıt geneli setine ("Product") düşmesi gerekir.
/// Geri düşüş sırası bozulursa istisna fırlamaz: Mavi yüzük satırında Kırmızı thumbnail görünür ya da
/// görseli olan varyant boş kutuyla listelenir. Poster'sız medyada (video karesi henüz yakalanmamış /
/// legacy kayıt) URL tamamen kaybolmamalı — içerik adresine düşer. Dört kural burada kilitlenir.</para>
/// </summary>
public abstract class OrderLineProductSnapshotBuilderTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly OrderLineProductSnapshotBuilder _builder;
    private readonly IRepository<EntityVariant, Guid> _variantRepository;
    private readonly IRepository<Media, Guid> _mediaRepository;
    private readonly IRepository<EntityMediaLink, Guid> _linkRepository;
    private readonly ICurrentCompany _currentCompany;

    protected OrderLineProductSnapshotBuilderTests()
    {
        _builder = GetRequiredService<OrderLineProductSnapshotBuilder>();
        _variantRepository = GetRequiredService<IRepository<EntityVariant, Guid>>();
        _mediaRepository = GetRequiredService<IRepository<Media, Guid>>();
        _linkRepository = GetRequiredService<IRepository<EntityMediaLink, Guid>>();
        _currentCompany = GetRequiredService<ICurrentCompany>();
    }

    [Fact]
    public async Task Variant_context_media_wins_over_product_media()
    {
        // Varyantın KENDİ medyası varken ürünün kayıt geneli medyası TUZAKTIR: geri düşüş sırası ters
        // kurulursa satırda yanlış SKU'nun fotoğrafı görünür. İki bağlama FARKLI medya kurulur, Id assert edilir.
        var companyId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        using (_currentCompany.Change(companyId))
        {
            var variant = await SeedVariantAsync(companyId, productId);
            var productMediaId = await SeedMediaAsync(companyId, MediaEntityNames.Product, productId, hasPoster: true);
            var variantMediaId = await SeedMediaAsync(companyId, MediaEntityNames.ProductVariant, variant.Id, hasPoster: true);

            var (_, imageUrl) = await _builder.BuildAsync(variant);

            imageUrl.ShouldNotBeNull();
            imageUrl.ShouldStartWith($"/api/media/{variantMediaId}/poster");
            imageUrl.ShouldNotContain(productMediaId.ToString());
        }
    }

    [Fact]
    public async Task Falls_back_to_product_media_when_variant_context_is_empty()
    {
        // Varyanta özel medya kurulmamışsa (tipik: tek görselli basit ürün) kayıt geneli set devreye girer —
        // görseli olan varyant boş kutuyla listelenmemeli.
        var companyId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        using (_currentCompany.Change(companyId))
        {
            var variant = await SeedVariantAsync(companyId, productId);
            var productMediaId = await SeedMediaAsync(companyId, MediaEntityNames.Product, productId, hasPoster: true);

            var (_, imageUrl) = await _builder.BuildAsync(variant);

            imageUrl.ShouldNotBeNull();
            imageUrl.ShouldStartWith($"/api/media/{productMediaId}/poster");
        }
    }

    [Fact]
    public async Task Returns_name_with_null_image_when_both_contexts_are_empty()
    {
        // Görselsiz ürün meşru bir durumdur (yeni açılmış kayıt) — isim DAİMA dolu döner, görsel null'dur;
        // istisna fırlamaz, uydurma placeholder URL üretilmez.
        var companyId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        using (_currentCompany.Change(companyId))
        {
            var variant = await SeedVariantAsync(companyId, productId);

            var (name, imageUrl) = await _builder.BuildAsync(variant);

            name.ShouldBe(variant.Name);
            imageUrl.ShouldBeNull();
        }
    }

    [Fact]
    public async Task Posterless_media_falls_back_to_content_url()
    {
        // Poster henüz yoksa (video karesi yakalanmamış / SetPoster'sız kayıt) PosterUrl null'dur —
        // snapshot bu durumda içerik adresine düşer; satır görselsiz kalmaz.
        var companyId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        using (_currentCompany.Change(companyId))
        {
            var variant = await SeedVariantAsync(companyId, productId);
            var mediaId = await SeedMediaAsync(companyId, MediaEntityNames.ProductVariant, variant.Id, hasPoster: false);

            var (_, imageUrl) = await _builder.BuildAsync(variant);

            imageUrl.ShouldBe($"/api/media/{mediaId}/content");
        }
    }

    /// <summary>Sahip ürünü temsil eden varyantı kurar (Id ABP tarafından atanır — builder varyantın
    /// Id'siyle "ProductVariant" bağlamını sorgular). EntityVariant polimorfiktir; ürün SATIRI gerekmez.</summary>
    private async Task<EntityVariant> SeedVariantAsync(Guid companyId, Guid productId)
    {
        return await WithUnitOfWorkAsync(async () =>
            await _variantRepository.InsertAsync(
                new EntityVariant(companyId, MediaEntityNames.Product, productId, "MAIN", "Mavi Yuzuk", isMain: true, isActive: true),
                autoSave: true));
    }

    /// <summary>Verilen bağlama (entityName + entityId) tek aktif COVER medyası kurar; medya Id'sini döner.
    /// <paramref name="hasPoster"/> false ise poster blob'u ayarlanmaz → PosterUrl null → ContentUrl beklenir.</summary>
    private async Task<Guid> SeedMediaAsync(Guid companyId, string entityName, Guid entityId, bool hasPoster)
    {
        return await WithUnitOfWorkAsync(async () =>
        {
            var media = new Media(
                companyId,
                MediaType.Image,
                blobName: Guid.NewGuid().ToString("N"),
                fileName: "photo.jpg",
                contentType: "image/jpeg",
                size: 1024,
                contentHash: Guid.NewGuid().ToString("N"));
            if (hasPoster)
            {
                media.SetPoster(Guid.NewGuid().ToString("N") + ".jpg");
            }

            await _mediaRepository.InsertAsync(media, autoSave: true);

            await _linkRepository.InsertAsync(
                new EntityMediaLink(companyId, entityName, entityId, media.Id, displayOrder: 0, isDefault: true, isActive: true),
                autoSave: true);

            return media.Id;
        });
    }
}
