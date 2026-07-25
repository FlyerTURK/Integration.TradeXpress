using Integration.TradeXpress.Products;
using Shouldly;
using Volo.Abp.Guids;
using Xunit;

namespace Integration.TradeXpress.Metals;

/// <summary>
/// Metal TEK temsili görsel davranışı — <c>SetImage</c>/<c>ClearImage</c>: kaynağı boş görsel temizlenir,
/// alanlar trim'lenir, karşı kaynağın alanları taşınmaz (bayat değer JSON'a persist olmasın).
/// </summary>
public class MetalImageTests
{
    private static Metal New()
    {
        // companyId artık ZORUNLU (ICompanyOwned — sahipsiz emtia kaydı yok).
        return new Metal(
            "HAS", "Has Altın",
            followingUnitId: SimpleGuidGenerator.Instance.Create(),
            companyId: SimpleGuidGenerator.Instance.Create());
    }

    [Fact]
    public void New_metal_has_no_image()
    {
        New().Image.ShouldBeNull();
    }

    [Fact]
    public void SetImage_url_source_trims_and_clears_upload_fields()
    {
        var metal = New();

        metal.SetImage(new MetalImage(
            ProductImageSourceType.Url, "  https://example.com/pic.jpg  ", "stale-blob", "stale.png"));

        metal.Image.ShouldNotBeNull();
        metal.Image!.SourceType.ShouldBe(ProductImageSourceType.Url);
        metal.Image.Url.ShouldBe("https://example.com/pic.jpg");
        metal.Image.BlobName.ShouldBeNull();     // karşı kaynağın alanı taşınmaz
        metal.Image.FileName.ShouldBeNull();
    }

    [Fact]
    public void SetImage_upload_source_trims_and_clears_url()
    {
        var metal = New();

        metal.SetImage(new MetalImage(
            ProductImageSourceType.Upload, "https://stale.example.com", " blob123.png ", " foto.png "));

        metal.Image.ShouldNotBeNull();
        metal.Image!.SourceType.ShouldBe(ProductImageSourceType.Upload);
        metal.Image.BlobName.ShouldBe("blob123.png");
        metal.Image.FileName.ShouldBe("foto.png");
        metal.Image.Url.ShouldBeNull();           // karşı kaynağın alanı taşınmaz
    }

    [Fact]
    public void SetImage_with_empty_source_clears_image()
    {
        var metal = New();
        metal.SetImage(new MetalImage(ProductImageSourceType.Url, "https://example.com/pic.jpg", null, null));

        // Url tipi ama URL boş → temizlenmiş sayılır.
        metal.SetImage(new MetalImage(ProductImageSourceType.Url, "   ", null, null));
        metal.Image.ShouldBeNull();

        // Upload tipi ama blob adı boş → temizlenmiş sayılır.
        metal.SetImage(new MetalImage(ProductImageSourceType.Upload, null, "", "foto.png"));
        metal.Image.ShouldBeNull();

        // null görsel → temizlenmiş sayılır.
        metal.SetImage(null);
        metal.Image.ShouldBeNull();
    }

    [Fact]
    public void SetImage_with_unknown_source_type_clears_image()
    {
        var metal = New();

        metal.SetImage(new MetalImage((ProductImageSourceType)99, "https://example.com/pic.jpg", "blob", "f.png"));

        metal.Image.ShouldBeNull();
    }

    [Fact]
    public void SetImage_upload_without_file_name_keeps_null_file_name()
    {
        var metal = New();

        metal.SetImage(new MetalImage(ProductImageSourceType.Upload, null, "blob123.png", "  "));

        metal.Image.ShouldNotBeNull();
        metal.Image!.FileName.ShouldBeNull();
    }

    [Fact]
    public void ClearImage_removes_image()
    {
        var metal = New();
        metal.SetImage(new MetalImage(ProductImageSourceType.Url, "https://example.com/pic.jpg", null, null));
        metal.Image.ShouldNotBeNull();

        metal.ClearImage();

        metal.Image.ShouldBeNull();
    }
}
