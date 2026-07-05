using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.Products;

/// <summary>
/// Product CRUD — <b>company-owned + per-tenant</b> katalog (AssayOffice company-scope + Account graf-save deseni
/// birleşimi). Kapsam DAİMA çalışılan şirket (<see cref="ICurrentCompany"/>; sunucu zorlar — client CompanyId
/// GÖNDERMEZ). Kimlik (Code uppercase normalize, şirket-scope benzersizlik). Varyantlar in-memory grafla yönetilir
/// (add/update/soft-delete); ürün başına en-az-1 + tekil-main değişmezi <see cref="ProductVariantManager"/>'da.
/// </summary>
[Authorize(TradeXpressPermissions.Products.Default)]
public class ProductAppService : TradeXpressAppService, IProductAppService
{
    private readonly IRepository<Product, Guid> _repository;
    private readonly IRepository<ProductVariant, Guid> _variantRepository;
    private readonly ProductVariantManager _variantManager;
    private readonly ICurrentCompany _currentCompany;

    private static readonly HashSet<string> AllowedListFields =
        new(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "IsActive", "Id" };

    public ProductAppService(
        IRepository<Product, Guid> repository,
        IRepository<ProductVariant, Guid> variantRepository,
        ProductVariantManager variantManager,
        ICurrentCompany currentCompany)
    {
        _repository = repository;
        _variantRepository = variantRepository;
        _variantManager = variantManager;
        _currentCompany = currentCompany;
    }

    public virtual async Task<PagedResultDto<ProductListDto>> GetListAsync(ProductListRequestDto input)
    {
        if (_currentCompany.Id is not { } companyId)
            return new PagedResultDto<ProductListDto>(0, new List<ProductListDto>());

        var query = (await _repository.GetQueryableAsync())
            .Where(x => x.CompanyId == companyId)
            .ApplyListRequest(input, AllowedListFields);

        var totalCount = await AsyncExecuter.CountAsync(query);
        var items = await AsyncExecuter.ToListAsync(query.Skip(input.SkipCount).Take(input.MaxResultCount));

        var counts = await LoadVariantCountsAsync(items.Select(p => p.Id));

        return new PagedResultDto<ProductListDto>(
            totalCount,
            items.Select(p => new ProductListDto
            {
                Id = p.Id,
                Code = p.Code,
                Name = p.Name,
                IsActive = p.IsActive,
                VariantCount = counts.GetValueOrDefault(p.Id),
            }).ToList());
    }

    public virtual async Task<ProductGetDto> GetAsync(Guid id) => await ToGetDtoAsync(await _repository.GetAsync(id));

    [Authorize(TradeXpressPermissions.Products.Create)]
    public virtual async Task<ProductGetDto> CreateAsync(ProductCreateDto input)
    {
        if (_currentCompany.Id is not { } companyId)
            throw new BusinessException("TradeXpress:Product:CompanyRequired");

        // Benzersizlik ÖN-kontrolü (Update ile simetrik): aynı şirkette aynı kodlu ürün → dostane hata,
        // ham DB (TenantId, CompanyId, Code) unique çakışması değil. Kendisi yok → excludeId boş.
        var normalizedCode = StringFieldGuard.NormalizeCode(
            input.Code, nameof(Product.Code), EntityFieldConsts.CodeMinLength, ProductConsts.CodeMaxLength);
        await EnsureCodeUniqueAsync(companyId, normalizedCode, Guid.Empty);

        var entity = new Product(companyId, input.Code, input.Name);
        entity.SetDescription(input.Description);
        await _repository.InsertAsync(entity, autoSave: true);

        await SaveVariantsAsync(entity, input.Variants);
        await _variantManager.EnsureMainVariantAsync(entity);   // en-az-1 + tekil-main garantisi
        return await ToGetDtoAsync(entity);
    }

    [Authorize(TradeXpressPermissions.Products.Update)]
    public virtual async Task<ProductGetDto> UpdateAsync(Guid id, ProductUpdateDto input)
    {
        var entity = await _repository.GetAsync(id);
        await ApplyCodeChangeAsync(entity, input.Code);
        entity.SetName(input.Name);
        entity.SetDescription(input.Description);
        entity.SetActive(input.IsActive);
        await _repository.UpdateAsync(entity, autoSave: true);

        await SaveVariantsAsync(entity, input.Variants);
        await _variantManager.EnsureMainVariantAsync(entity);   // hiçbir koşulda varyantsız/main'siz kalmasın
        return await ToGetDtoAsync(entity);
    }

    [Authorize(TradeXpressPermissions.Products.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        // Güvenlik sınırı (Account deseni): ürünü ÖNCE yükle — company query filter yabancı şirketin ürününü
        // gizler → EntityNotFoundException. Doğrulama varyant silmeden ÖNCE olmalı.
        var entity = await _repository.GetAsync(id);
        await _variantManager.DeleteVariantsOfProductAsync(entity.Id);
        await _repository.DeleteAsync(entity, autoSave: true);
    }

    /// <summary>Kod değişikliği (ürün kuralı 2026-07-04): normalize et → değiştiyse AYNI ŞİRKET altında
    /// benzersizliği doğrula (kendisi hariç; dostane hata) → uygula.</summary>
    private async Task ApplyCodeChangeAsync(Product entity, string rawCode)
    {
        var normalizedCode = StringFieldGuard.NormalizeCode(
            rawCode, nameof(entity.Code), EntityFieldConsts.CodeMinLength, ProductConsts.CodeMaxLength);
        if (string.Equals(normalizedCode, entity.Code, StringComparison.Ordinal))
        {
            return; // değişmedi
        }

        await EnsureCodeUniqueAsync(entity.CompanyId, normalizedCode, entity.Id);
        entity.SetCode(normalizedCode);
    }

    /// <summary>Aynı ŞİRKET altında Code benzersizliği ((TenantId, CompanyId, Code) unique index'iyle hizalı).
    /// Create'te <paramref name="excludeId"/>=Guid.Empty, Update'te entity.Id. Dostane BusinessException.</summary>
    private async Task EnsureCodeUniqueAsync(Guid companyId, string normalizedCode, Guid excludeId)
    {
        var duplicate = await AsyncExecuter.AnyAsync(
            (await _repository.GetQueryableAsync())
                .Where(p => p.CompanyId == companyId && p.Id != excludeId && p.Code == normalizedCode));
        if (duplicate)
        {
            throw new BusinessException("TradeXpress:Product:CodeAlreadyExists");
        }
    }

    // ── varyant grafı diff (Id + IsDeleted) — repository üzerinden doğrudan (Account SaveSubAccountsAsync deseni) ──
    private async Task SaveVariantsAsync(Product product, List<ProductVariantGraphDto> variants)
    {
        if (variants == null) return;

        // Önce ekle + güncelle, sonra sil (Account/Branch→Vault deseniyle aynı).
        foreach (var v in variants.Where(x => !x.IsDeleted))
        {
            if (v.Id == Guid.Empty)
            {
                await InsertVariantAsync(product, v);
            }
            else
            {
                await UpdateVariantAsync(product, v);
            }
        }

        foreach (var v in variants.Where(x => x.IsDeleted && x.Id != Guid.Empty))
        {
            await _variantRepository.DeleteAsync(v.Id, autoSave: true);
        }
    }

    private async Task InsertVariantAsync(Product product, ProductVariantGraphDto v)
    {
        var normalizedCode = StringFieldGuard.NormalizeCode(
            v.Code, nameof(ProductVariant.Code), EntityFieldConsts.CodeMinLength, ProductConsts.CodeMaxLength);
        await EnsureVariantCodeUniqueAsync(product.Id, normalizedCode, Guid.Empty);

        // Şirket parent üründen DENORMALİZE. Main manager'a bırakılır (IsMain drill'den seçilmez → isMain:false).
        var variant = new ProductVariant(product.CompanyId, product.Id, v.Code, v.Name, isMain: false, isActive: v.IsActive);
        variant.SetDescription(v.Description);
        await _variantRepository.InsertAsync(variant, autoSave: true);
    }

    private async Task UpdateVariantAsync(Product product, ProductVariantGraphDto v)
    {
        var variant = await _variantRepository.GetAsync(v.Id);

        var normalizedCode = StringFieldGuard.NormalizeCode(
            v.Code, nameof(ProductVariant.Code), EntityFieldConsts.CodeMinLength, ProductConsts.CodeMaxLength);
        if (!string.Equals(normalizedCode, variant.Code, StringComparison.Ordinal))
        {
            await EnsureVariantCodeUniqueAsync(product.Id, normalizedCode, variant.Id);
            variant.SetCode(normalizedCode);
        }

        variant.SetName(v.Name);
        variant.SetDescription(v.Description);
        variant.SetActive(v.IsActive);
        // IsMain'e DOKUNULMAZ (display-only; değişmez manager'da). Mevcut main korunur.
        await _variantRepository.UpdateAsync(variant, autoSave: true);
    }

    /// <summary>Aynı ÜRÜN altında varyant Code benzersizliği. Dostane BusinessException — ham DB çakışmasını önler.</summary>
    private async Task EnsureVariantCodeUniqueAsync(Guid productId, string normalizedCode, Guid excludeId)
    {
        var duplicate = await AsyncExecuter.AnyAsync(
            (await _variantRepository.GetQueryableAsync())
                .Where(x => x.ProductId == productId && x.Id != excludeId && x.Code == normalizedCode));
        if (duplicate)
        {
            throw new BusinessException("TradeXpress:ProductVariant:CodeAlreadyExists");
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<Dictionary<Guid, int>> LoadVariantCountsAsync(IEnumerable<Guid> productIds)
    {
        var ids = productIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, int>();

        var grouped = await AsyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync())
                .Where(v => ids.Contains(v.ProductId))
                .GroupBy(v => v.ProductId)
                .Select(g => new { ProductId = g.Key, Count = g.Count() }));
        return grouped.ToDictionary(x => x.ProductId, x => x.Count);
    }

    private async Task<ProductGetDto> ToGetDtoAsync(Product p)
    {
        var variants = await AsyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync()).Where(v => v.ProductId == p.Id).OrderBy(v => v.Code));

        return new ProductGetDto
        {
            Id = p.Id,
            Code = p.Code,
            Name = p.Name,
            Description = p.Description,
            IsActive = p.IsActive,
            Variants = variants.Select(v => new ProductVariantGraphDto
            {
                Id = v.Id,
                IsMain = v.IsMain,
                Code = v.Code,
                Name = v.Name,
                Description = v.Description,
                IsActive = v.IsActive,
            }).ToList(),
        };
    }
}
