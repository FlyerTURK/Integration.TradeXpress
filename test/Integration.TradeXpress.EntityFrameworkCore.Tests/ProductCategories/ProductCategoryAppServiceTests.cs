using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.MultiCompany;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.ProductCategories;

/// <summary>
/// Kategori ağacının DB'ye dokunan kuralları — saf testlerle sınanamayanlar (hepsi repository ister).
///
/// <para><b>Neden gerekli:</b> <c>Code</c> kaldırıldığında kategorinin kimliği ADI oldu ve benzersizlik
/// KARDEŞ düzeyine indi (2026-07-27). Bu kural, döngü guard'ı ve "çocuğu olan silinemez" kuralı yalnız
/// AppService/manager içinde yaşıyordu; hiçbiri sınanmıyordu. Kökler için benzersizlik ayrıca EF Core'un
/// null semantiğine dayanır (<c>ParentId == null-parametre</c>) — o davranış bozulursa iki kök aynı adı
/// alabilirdi ve kimse fark etmezdi.</para>
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class ProductCategoryAppServiceTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly IProductCategoryAppService _appService;
    private readonly Products.IProductAppService _productAppService;
    private readonly ICurrentTenant _currentTenant;
    private readonly TestCompanyContextProvider _companyContext;

    public ProductCategoryAppServiceTests()
    {
        _appService     = GetRequiredService<IProductCategoryAppService>();
        _productAppService = GetRequiredService<Products.IProductAppService>();
        _currentTenant  = GetRequiredService<ICurrentTenant>();
        _companyContext = GetRequiredService<TestCompanyContextProvider>();
    }

    [Fact]
    public async Task Two_children_of_the_same_parent_cannot_share_a_name()
    {
        await InCompanyAsync(async () =>
        {
            var parent = await CreateAsync("Takı");
            await CreateAsync("Yüzük", parent.Id);

            var error = await Should.ThrowAsync<BusinessException>(() => CreateAsync("Yüzük", parent.Id));
            error.Code.ShouldBe("TradeXpress:ProductCategory:NameAlreadyExists");
        });
    }

    [Fact]
    public async Task Two_root_categories_cannot_share_a_name()
    {
        // KÖKLER İÇİN KRİTİK: kontrol "ParentId == null-parametre" karşılaştırmasına dayanır. EF Core'un null
        // semantiği bunu IS NULL'a çevirmezse sorgu HİÇBİR ZAMAN eşleşmez, ön-kontrol sessizce etkisizleşir ve
        // kullanıcı ham DB unique hatası görürdü. Bu test o çeviriyi mekanik olarak sabitler.
        await InCompanyAsync(async () =>
        {
            await CreateAsync("Takı");

            var error = await Should.ThrowAsync<BusinessException>(() => CreateAsync("Takı"));
            error.Code.ShouldBe("TradeXpress:ProductCategory:NameAlreadyExists");
        });
    }

    [Fact]
    public async Task The_same_name_is_allowed_under_different_parents()
    {
        // Kardeş-benzersizliğini seçmemizin SEBEBİ bu: "Takı › Yüzük" ile "Saat › Yüzük" ikisi de meşrudur.
        // Şirket-geneli benzersizlik (eski Code davranışı) bunu haksız yere engellerdi.
        await InCompanyAsync(async () =>
        {
            var jewelry = await CreateAsync("Takı");
            var watch = await CreateAsync("Saat");

            await CreateAsync("Yüzük", jewelry.Id);
            var second = await CreateAsync("Yüzük", watch.Id);

            second.Path.ShouldBe("Saat › Yüzük");
        });
    }

    [Fact]
    public async Task Name_clash_is_detected_after_normalization()
    {
        // Ön-kontrol entity'nin uyguladığı normalizasyonun aynısını kullanmalı; kullanmasaydı "  yüzük "
        // ön-kontrolden geçip DB'de "Yüzük" ile çakışır ve ham hata olarak inerdi.
        await InCompanyAsync(async () =>
        {
            var parent = await CreateAsync("Takı");
            await CreateAsync("Yüzük", parent.Id);

            var error = await Should.ThrowAsync<BusinessException>(() => CreateAsync("  yüzük  ", parent.Id));
            error.Code.ShouldBe("TradeXpress:ProductCategory:NameAlreadyExists");
        });
    }

    [Fact]
    public async Task Moving_a_category_into_a_name_clash_is_rejected()
    {
        // Ad değişmiyor, ÜST değişiyor: kontrol yalnız ad değişimine bakıyor olsaydı bu kaçardı.
        await InCompanyAsync(async () =>
        {
            var jewelry = await CreateAsync("Takı");
            var watch = await CreateAsync("Saat");
            await CreateAsync("Yüzük", jewelry.Id);
            var moving = await CreateAsync("Yüzük", watch.Id);

            var error = await Should.ThrowAsync<BusinessException>(
                () => UpdateAsync(moving, parentId: jewelry.Id));
            error.Code.ShouldBe("TradeXpress:ProductCategory:NameAlreadyExists");
        });
    }

    [Fact]
    public async Task A_category_cannot_be_moved_under_its_own_descendant()
    {
        // Döngü guard'ı: Takı → Yüzük → Alyans zincirinde Takı'yı Alyans'ın altına almak ağacı kilitlerdi.
        await InCompanyAsync(async () =>
        {
            var root = await CreateAsync("Takı");
            var child = await CreateAsync("Yüzük", root.Id);
            var grandChild = await CreateAsync("Alyans", child.Id);

            var error = await Should.ThrowAsync<BusinessException>(
                () => UpdateAsync(root, parentId: grandChild.Id));
            error.Code.ShouldBe("TradeXpress:ProductCategory:CircularParent");
        });
    }

    [Fact]
    public async Task A_category_cannot_become_its_own_parent()
    {
        await InCompanyAsync(async () =>
        {
            var category = await CreateAsync("Takı");

            var error = await Should.ThrowAsync<BusinessException>(
                () => UpdateAsync(category, parentId: category.Id));
            error.Code.ShouldBe("TradeXpress:ProductCategory:CannotBeOwnParent");
        });
    }

    [Fact]
    public async Task A_category_with_children_cannot_be_deleted()
    {
        // Silinseydi alt dal öksüz kalır ve hiçbir ekranda görünmezdi.
        await InCompanyAsync(async () =>
        {
            var parent = await CreateAsync("Takı");
            await CreateAsync("Yüzük", parent.Id);

            var error = await Should.ThrowAsync<BusinessException>(() => _appService.DeleteAsync(parent.Id));
            error.Code.ShouldBe("TradeXpress:ProductCategory:HasChildren");
        });
    }

    [Fact]
    public async Task Parent_options_exclude_the_category_itself_and_its_whole_subtree()
    {
        // "Üst kategoriler alt kategorilerinden üst kategori seçemesin" (2026-07-27 Hakan) — combo'ya hangi
        // satırların gittiği burada sabitleniyor; kullanıcıya döngü kurduran seçenek hiç gösterilmemeli.
        await InCompanyAsync(async () =>
        {
            var root = await CreateAsync("Takı");
            var child = await CreateAsync("Yüzük", root.Id);
            var grandChild = await CreateAsync("Alyans", child.Id);
            var unrelated = await CreateAsync("Saat");

            var options = await _appService.GetParentOptionsAsync(root.Id);
            var ids = options.Select(o => o.Id).ToList();

            ids.ShouldNotContain(root.Id);
            ids.ShouldNotContain(child.Id);
            ids.ShouldNotContain(grandChild.Id);
            ids.ShouldContain(unrelated.Id);   // ayrık dal MEŞRU aday — gereksiz dışlamak kullanıcıyı engellerdi
        });
    }

    [Fact]
    public async Task Parent_options_keep_the_current_parent_even_when_it_is_inactive()
    {
        // Üstü pasifleşmiş bir kategoriyi düzenlerken combo BOŞ görünmemeli: kullanıcı üstünü kaybetmiş
        // sanıp yeniden seçmeye kalkar ya da farkında olmadan kökte bırakır.
        await InCompanyAsync(async () =>
        {
            var parent = await CreateAsync("Takı");
            var child = await CreateAsync("Yüzük", parent.Id);

            await UpdateAsync(parent, isActive: false);

            var options = await _appService.GetParentOptionsAsync(child.Id);

            options.Select(o => o.Id).ShouldContain(parent.Id);
        });
    }

    [Fact]
    public async Task Effective_attributes_include_values_inherited_from_ancestors()
    {
        // Uçtan uca kalıtım: üstteki değerler alt kategoride görünmeli ve alt kategori aynı adlı niteliği
        // yeniden tanımlasa bile üstten gelenler DÜŞMEMELİ (ekleyerek birleşme).
        await InCompanyAsync(async () =>
        {
            var root = await CreateAsync("Takı", attributes: new List<ProductCategoryAttributeDto>
            {
                new()
                {
                    Name = "Ayar",
                    Values = new List<ProductCategoryAttributeValueDto> { new() { Value = "14K" }, new() { Value = "18K" } },
                },
            });

            var child = await CreateAsync("Yüzük", root.Id, attributes: new List<ProductCategoryAttributeDto>
            {
                new()
                {
                    Name = "Ayar",
                    Values = new List<ProductCategoryAttributeValueDto> { new() { Value = "22K" } },
                },
            });

            var effective = await _appService.GetEffectiveAttributesAsync(child.Id);

            var ayar = effective.ShouldHaveSingleItem();
            ayar.Values.Select(v => v.Value).ShouldBe(new[] { "14K", "18K", "22K" });
            ayar.Values.Single(v => v.Value == "14K").IsInherited.ShouldBeTrue();
            ayar.Values.Single(v => v.Value == "22K").IsInherited.ShouldBeFalse();
        });
    }

    [Fact]
    public async Task Inherited_attributes_appear_in_the_child_grid_marked_and_locked()
    {
        // 2026-07-28 Hakan: "üst kategorinin attribute listesi attributes gridinde silinemez olarak otomatik
        // eklenmeli". Alt kategori HİÇ nitelik tanımlamamış olsa bile üstünkileri görmeli.
        await InCompanyAsync(async () =>
        {
            var root = await CreateAsync("Takı", attributes: new List<ProductCategoryAttributeDto>
            {
                new()
                {
                    Name = "Ayar",
                    Values = new List<ProductCategoryAttributeValueDto> { new() { Value = "14K" }, new() { Value = "18K" } },
                },
            });

            var child = await CreateAsync("Yüzük", root.Id);
            var reloaded = await _appService.GetAsync(child.Id);

            var inherited = reloaded.Attributes.ShouldHaveSingleItem();
            inherited.Name.ShouldBe("Ayar");
            inherited.IsInherited.ShouldBeTrue();
            inherited.SourceCategoryName.ShouldBe("Takı");
            inherited.Values.Select(v => v.Value).ShouldBe(new[] { "14K", "18K" });
            inherited.Values.ShouldAllBe(v => v.IsInherited);
        });
    }

    [Fact]
    public async Task Saving_a_child_does_not_copy_inherited_attributes_into_it()
    {
        // Grid'de görünen devralınan satır olduğu gibi geri gönderilir (kullanıcı hiçbir şey değiştirmedi).
        // Kaydetme onları BU kategoriye kopyalamamalı — yoksa kalıtım anlamını yitirir ve üstteki bir düzeltme
        // alt kategorilere yansımaz olurdu.
        await InCompanyAsync(async () =>
        {
            var root = await CreateAsync("Takı", attributes: new List<ProductCategoryAttributeDto>
            {
                new()
                {
                    Name = "Ayar",
                    Values = new List<ProductCategoryAttributeValueDto> { new() { Value = "14K" } },
                },
            });

            var child = await CreateAsync("Yüzük", root.Id);
            var loaded = await _appService.GetAsync(child.Id);

            var saved = await UpdateAsync(loaded);          // formu değiştirmeden kaydet
            var reloaded = await _appService.GetAsync(child.Id);

            saved.Attributes.ShouldHaveSingleItem().IsInherited.ShouldBeTrue();
            reloaded.Attributes.ShouldHaveSingleItem().IsInherited.ShouldBeTrue();

            // Üstteki niteliğe dokunulmadı: hâlâ TEK sahibi var.
            var rootReloaded = await _appService.GetAsync(root.Id);
            rootReloaded.Attributes.ShouldHaveSingleItem().IsInherited.ShouldBeFalse();
        });
    }

    [Fact]
    public async Task Adding_an_own_value_to_an_inherited_attribute_merges_instead_of_duplicating()
    {
        // Kullanıcı devralınan "Ayar"a 22K ekliyor. Sonuç TEK "Ayar" satırı olmalı: 14K/18K devralınan,
        // 22K kendi. İki ayrı "Ayar" satırı görünseydi kalıtım kullanıcıya sızmış olurdu.
        await InCompanyAsync(async () =>
        {
            var root = await CreateAsync("Takı", attributes: new List<ProductCategoryAttributeDto>
            {
                new()
                {
                    Name = "Ayar",
                    Values = new List<ProductCategoryAttributeValueDto> { new() { Value = "14K" }, new() { Value = "18K" } },
                },
            });

            var child = await CreateAsync("Yüzük", root.Id);
            var loaded = await _appService.GetAsync(child.Id);

            loaded.Attributes.Single().Values.Add(new ProductCategoryAttributeValueDto { Value = "22K" });
            await UpdateAsync(loaded);

            var reloaded = await _appService.GetAsync(child.Id);

            var ayar = reloaded.Attributes.ShouldHaveSingleItem();
            ayar.Values.Select(v => v.Value).ShouldBe(new[] { "14K", "18K", "22K" });
            ayar.Values.Single(v => v.Value == "14K").IsInherited.ShouldBeTrue();
            ayar.Values.Single(v => v.Value == "22K").IsInherited.ShouldBeFalse();

            // Üst kategori DEĞİŞMEDİ — 22K oraya sızmadı.
            var rootReloaded = await _appService.GetAsync(root.Id);
            rootReloaded.Attributes.Single().Values.Select(v => v.Value).ShouldBe(new[] { "14K", "18K" });
        });
    }

    [Fact]
    public async Task Preview_brings_the_whole_ancestor_chain_not_just_the_direct_parent()
    {
        // 2026-07-28 Hakan sorusu: "üst kategori seçtiğimde o üst kategori de onun üstündeki tüm
        // kategorilerden aldığı attribute ve value'leri kalıtım almış olarak geliyor değil mi?" — EVET.
        // Bu test onu kaydetmeden ÖNCEKİ önizleme yolunda sabitler.
        await InCompanyAsync(async () =>
        {
            var root = await CreateAsync("Takı", attributes: new List<ProductCategoryAttributeDto>
            {
                new() { Name = "Materyal", Values = new List<ProductCategoryAttributeValueDto> { new() { Value = "Altın" } } },
            });

            var middle = await CreateAsync("Yüzük", root.Id, attributes: new List<ProductCategoryAttributeDto>
            {
                new() { Name = "Ayar", Values = new List<ProductCategoryAttributeValueDto> { new() { Value = "14K" } } },
            });

            // Henüz kaydedilmemiş bir kategori "Yüzük"ü üst seçiyor → hem Yüzük'ün hem Takı'nın nitelikleri.
            var preview = await _appService.PreviewInheritanceAsync(new ProductCategoryInheritancePreviewDto
            {
                ParentId = middle.Id,
                OwnAttributes = new List<ProductCategoryAttributeDto>(),
            });

            preview.Select(a => a.Name).ShouldBe(new[] { "Materyal", "Ayar" }, ignoreOrder: true);
            preview.ShouldAllBe(a => a.IsInherited);
            preview.Single(a => a.Name == "Materyal").SourceCategoryName.ShouldBe("Takı");
            preview.Single(a => a.Name == "Ayar").SourceCategoryName.ShouldBe("Yüzük");
        });
    }

    [Fact]
    public async Task Preview_keeps_the_identity_of_existing_own_attributes()
    {
        // KRİTİK: önizleme kaydedilmeyen bir taslak üzerinden hesaplanır. Kendi satırların kimliği geri
        // yazılmazsa bir sonraki kaydetmede yeni satır olarak yazılır ve pazaryeri eşleştirmeleri kopardı.
        await InCompanyAsync(async () =>
        {
            var root = await CreateAsync("Takı");
            var category = await CreateAsync("Yüzük", attributes: new List<ProductCategoryAttributeDto>
            {
                new() { Name = "Ayar", Values = new List<ProductCategoryAttributeValueDto> { new() { Value = "14K" } } },
            });

            var loaded = await _appService.GetAsync(category.Id);
            var attributeId = loaded.Attributes.Single().Id;
            var valueId = loaded.Attributes.Single().Values.Single().Id;

            attributeId.ShouldNotBe(Guid.Empty);

            var preview = await _appService.PreviewInheritanceAsync(new ProductCategoryInheritancePreviewDto
            {
                ParentId = root.Id,
                OwnAttributes = loaded.Attributes,
            });

            var own = preview.Single(a => a.Name == "Ayar");
            own.IsInherited.ShouldBeFalse();
            own.Id.ShouldBe(attributeId);
            own.Values.Single(v => v.Value == "14K").Id.ShouldBe(valueId);
        });
    }

    [Fact]
    public async Task Preview_of_a_root_category_has_no_inherited_attributes()
    {
        await InCompanyAsync(async () =>
        {
            var root = await CreateAsync("Takı", attributes: new List<ProductCategoryAttributeDto>
            {
                new() { Name = "Materyal", Values = new List<ProductCategoryAttributeValueDto> { new() { Value = "Altın" } } },
            });

            root.ShouldNotBeNull();

            var preview = await _appService.PreviewInheritanceAsync(new ProductCategoryInheritancePreviewDto
            {
                ParentId = null,
                OwnAttributes = new List<ProductCategoryAttributeDto>(),
            });

            preview.ShouldBeEmpty();
        });
    }

    [Fact]
    public async Task Attribute_identity_survives_an_update()
    {
        // Kanal eşleştirmesi bu kimliğe asılacak — kaydetme sırasında değişirse eşleştirmeler sessizce kopar.
        await InCompanyAsync(async () =>
        {
            var category = await CreateAsync("Takı", attributes: new List<ProductCategoryAttributeDto>
            {
                new()
                {
                    Name = "Ayar",
                    Values = new List<ProductCategoryAttributeValueDto> { new() { Value = "14K" } },
                },
            });

            var attributeId = category.Attributes.Single().Id;
            var valueId = category.Attributes.Single().Values.Single().Id;

            attributeId.ShouldNotBe(Guid.Empty);

            var updated = await UpdateAsync(category, mutate: dto =>
            {
                dto.Attributes.Single().Name = "Ayar (Karat)";
                dto.Attributes.Single().Values.Add(new ProductCategoryAttributeValueDto { Value = "18K" });
            });

            var attribute = updated.Attributes.ShouldHaveSingleItem();
            attribute.Id.ShouldBe(attributeId);
            attribute.Values.Single(v => v.Value == "14K").Id.ShouldBe(valueId);
            attribute.Values.Count.ShouldBe(2);
        });
    }

    [Fact]
    public async Task A_category_linked_to_products_cannot_be_deleted()
    {
        // Bağ id-only (sert FK yok) → DB engellemezdi; ürünler var olmayan bir kategoriyi işaret eder ve
        // o üründe kanal kategorisi/komisyon çözümü sessizce boşa düşerdi.
        await InCompanyAsync(async () =>
        {
            var category = await CreateAsync("Takı");
            await _productAppService.CreateAsync(new Products.ProductCreateDto
            {
                Code = "URN1",
                Name = "Tektaş Yüzük",
                ProductCategoryId = category.Id,
            });

            var error = await Should.ThrowAsync<BusinessException>(() => _appService.DeleteAsync(category.Id));
            error.Code.ShouldBe("TradeXpress:ProductCategory:InUseByProducts");
        });
    }

    [Fact]
    public async Task Product_rejects_a_category_from_another_company()
    {
        // Sahiplik sınırı: başka şirketin kategorisi id gönderilerek ele geçirilemez.
        var foreignCategoryId = Guid.Empty;

        await InCompanyAsync(async () =>
        {
            foreignCategoryId = (await CreateAsync("Yabancı Kategori")).Id;
        });

        await InCompanyAsync(async () =>
        {
            var error = await Should.ThrowAsync<BusinessException>(
                () => _productAppService.CreateAsync(new Products.ProductCreateDto
                {
                    Code = "URN2",
                    Name = "Kaçak Ürün",
                    ProductCategoryId = foreignCategoryId,
                }));
            error.Code.ShouldBe("TradeXpress:Product:ProductCategoryNotFound");
        });
    }

    [Fact]
    public async Task Product_cannot_be_saved_without_a_category()
    {
        // 2026-07-28 Hakan: bağ ZORUNLU oldu. Önceden isteğe bağlıydı ("sınıflandırılmamış ürün" serbestti);
        // kategorisiz ürünün kanal kategorisi ve komisyonu çözülemediğinden pazaryerine gidemiyor ve fiyatı
        // komisyonsuz — yani eksik — hesaplanıyordu. Hata vermediği için bu sessizce yanlış fiyat üretiyordu.
        await InCompanyAsync(async () =>
        {
            var error = await Should.ThrowAsync<BusinessException>(
                () => _productAppService.CreateAsync(new Products.ProductCreateDto
                {
                    Code = "URN3",
                    Name = "Kategorisiz Ürün",
                }));

            error.Code.ShouldBe("TradeXpress:Product:ProductCategoryRequired");
        });
    }

    /// <summary>
    /// Her test kendi tenant+şirketinde çalışır: kategori adları testler arası çakışmasın (kardeş benzersizliği
    /// şirket kapsamındadır) ve global filtre gerçekten devrede olsun.
    ///
    /// <para>Gövde KASITLA tek bir UnitOfWork'e sarılmaz: her AppService çağrısı kendi UoW'unu açar ve canlıda
    /// da her istek ayrı UoW'dur. Hepsini tek UoW'a sıkıştırmak change-tracker'ı testin kendisine özgü bir
    /// duruma sokar (aynı grafı hem yazıp hem okuduğumuzda kimlik çakışması) — yani üretimde olmayan bir
    /// hatayı sınamış olurduk.</para>
    /// </summary>
    private async Task InCompanyAsync(Func<Task> body)
    {
        var tenantId = SimpleGuidGenerator.Instance.Create();
        var companyId = SimpleGuidGenerator.Instance.Create();

        using (_currentTenant.Change(tenantId))
        {
            _companyContext.CompanyId = companyId;
            try
            {
                await body();
            }
            finally
            {
                _companyContext.CompanyId = null;
            }
        }
    }

    private Task<ProductCategoryGetDto> CreateAsync(
        string name,
        Guid? parentId = null,
        List<ProductCategoryAttributeDto>? attributes = null)
    {
        return _appService.CreateAsync(new ProductCategoryCreateDto
        {
            Name = name,
            ParentId = parentId,
            Attributes = attributes ?? new List<ProductCategoryAttributeDto>(),
        });
    }

    private Task<ProductCategoryGetDto> UpdateAsync(
        ProductCategoryGetDto source,
        Guid? parentId = null,
        bool? isActive = null,
        Action<ProductCategoryUpdateDto>? mutate = null)
    {
        var input = new ProductCategoryUpdateDto
        {
            Name = source.Name,
            ParentId = parentId ?? source.ParentId,
            IsActive = isActive ?? source.IsActive,
            DisplayOrder = source.DisplayOrder,
            Description = source.Description,
            Attributes = source.Attributes,
        };

        mutate?.Invoke(input);
        return _appService.UpdateAsync(source.Id, input);
    }
}
