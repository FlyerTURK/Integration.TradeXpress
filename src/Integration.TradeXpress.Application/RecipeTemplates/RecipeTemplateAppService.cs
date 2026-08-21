using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Products;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.RecipeTemplates;

/// <summary>
/// Reçete şablonu ("orta reçete") CRUD + ürüne uygulama — <b>company-owned</b>. Kapsam DAİMA çalışılan şirket.
///
/// <para>Satırlar MERGE edilir (kategori nitelikleriyle aynı semantik): gelen <c>Id</c> güncellenir, gelmeyen
/// silinir, boş <c>Id</c> yenidir. Satır kimliklerini korumak düzenleme geçmişini ve ileride kurulacak
/// referansları ayakta tutar.</para>
/// </summary>
[Authorize(TradeXpressPermissions.RecipeTemplates.Default)]
public class RecipeTemplateAppService : TradeXpressAppService, IRecipeTemplateAppService
{
    private readonly IRepository<RecipeTemplate, Guid> _repository;
    private readonly IRepository<Product, Guid> _productRepository;
    private readonly RecipeTemplateApplier _applier;
    private readonly ICurrentCompany _currentCompany;

    private static readonly HashSet<string> AllowedListFields =
        new(StringComparer.OrdinalIgnoreCase) { "Name", "IsActive", "DisplayOrder", "Id" };

    public RecipeTemplateAppService(
        IRepository<RecipeTemplate, Guid> repository,
        IRepository<Product, Guid> productRepository,
        RecipeTemplateApplier applier,
        ICurrentCompany currentCompany)
    {
        _repository = repository;
        _productRepository = productRepository;
        _applier = applier;
        _currentCompany = currentCompany;
    }

    public virtual async Task<PagedResultDto<RecipeTemplateListDto>> GetListAsync(RecipeTemplateListRequestDto input)
    {
        if (_currentCompany.Id is not { } companyId)
        {
            return new PagedResultDto<RecipeTemplateListDto>(0, new List<RecipeTemplateListDto>());
        }

        // WithDetailsAsync (GetQueryableAsync DEĞİL): satırlar olmadan LineCount DAİMA 0 dönerdi —
        // kullanıcı dolu şablonu "boş" sanardı. DefaultWithDetailsFunc Lines'ı Include eder.
        var query = (await _repository.WithDetailsAsync())
            .Where(x => x.CompanyId == companyId)
            .ApplyListRequest(input, AllowedListFields);

        var totalCount = await AsyncExecuter.CountAsync(query);
        var items = await AsyncExecuter.ToListAsync(query.ApplyPaging(input));

        return new PagedResultDto<RecipeTemplateListDto>(
            totalCount, items.Select(ToListDto).ToList());
    }

    public virtual async Task<RecipeTemplateGetDto> GetAsync(Guid id)
    {
        return ObjectMapper.Map<RecipeTemplate, RecipeTemplateGetDto>(await _repository.GetAsync(id));
    }

    [Authorize(TradeXpressPermissions.RecipeTemplates.Create)]
    public virtual async Task<RecipeTemplateGetDto> CreateAsync(RecipeTemplateCreateDto input)
    {
        // Sahiplik client'tan DEĞİL aktif working company'den (şirket yoksa fail-closed).
        var companyId = CompanyOwnershipGuard.ResolveOwnerCompanyId(_currentCompany);

        var normalizedName = NormalizeName(input.Name);
        await EnsureNameUniqueAsync(companyId, normalizedName, Guid.Empty);

        var entity = new RecipeTemplate(companyId, input.Name, input.DisplayOrder);
        entity.SetDescription(input.Description);
        RecipeTemplateLineMerger.Apply(entity, input.Lines);

        await _repository.InsertAsync(entity, autoSave: true);
        return ObjectMapper.Map<RecipeTemplate, RecipeTemplateGetDto>(entity);
    }

    [Authorize(TradeXpressPermissions.RecipeTemplates.Update)]
    public virtual async Task<RecipeTemplateGetDto> UpdateAsync(Guid id, RecipeTemplateUpdateDto input)
    {
        var entity = await _repository.GetAsync(id);

        var normalizedName = NormalizeName(input.Name);
        if (!string.Equals(normalizedName, entity.Name, StringComparison.Ordinal))
        {
            await EnsureNameUniqueAsync(entity.CompanyId, normalizedName, entity.Id);
        }

        entity.SetName(input.Name);
        entity.SetDescription(input.Description);
        entity.SetDisplayOrder(input.DisplayOrder);
        entity.SetActive(input.IsActive);
        RecipeTemplateLineMerger.Apply(entity, input.Lines);

        await _repository.UpdateAsync(entity, autoSave: true);
        return ObjectMapper.Map<RecipeTemplate, RecipeTemplateGetDto>(entity);
    }

    [Authorize(TradeXpressPermissions.RecipeTemplates.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        // Şablon ürünle KALICI bağ kurmaz (uygulanan satırlar ürüne kopyalanır) → silme guard'ı GEREKMEZ:
        // silinen şablon, daha önce uygulanmış satırları etkilemez.
        await _repository.DeleteAsync(id);
    }

    public virtual async Task<List<RecipeTemplateListDto>> GetPickerListAsync()
    {
        if (_currentCompany.Id is not { } companyId)
        {
            return new List<RecipeTemplateListDto>();
        }

        var rows = await AsyncExecuter.ToListAsync(
            (await _repository.WithDetailsAsync())
                .Where(x => x.CompanyId == companyId && x.IsActive)
                .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name));

        return rows.Select(ToListDto).ToList();
    }

    [Authorize(TradeXpressPermissions.Products.Update)]
    public virtual async Task<RecipeTemplateApplyResultDto> ApplyToProductAsync(Guid templateId, Guid productId)
    {
        var product = await _productRepository.GetAsync(productId);
        var template = await _repository.GetAsync(templateId);

        if (template.CompanyId != product.CompanyId)
        {
            throw new BusinessException("TradeXpress:RecipeTemplate:NotFound");
        }

        // Pasif şablon uygulanamaz: pasifleştirme "artık kullanılmasın" demektir; combo zaten yalnız aktifleri
        // gösterir ama doğrudan çağrı (API/eski sekme) bunu atlayabilirdi.
        if (!template.IsActive)
        {
            throw new BusinessException("TradeXpress:RecipeTemplate:Inactive");
        }

        var outcome = await _applier.ApplyToProductAsync(product, templateId);

        return new RecipeTemplateApplyResultDto
        {
            TemplateId = template.Id,
            TemplateName = template.Name,
            AffectedVariantCount = outcome.AffectedVariantCount,
            AppliedLineCount = template.Lines.Count,
            PreservedEditedLineCount = outcome.PreservedEditedLineCount,
        };
    }

    private static string NormalizeName(string rawName)
    {
        return StringFieldGuard.NormalizeName(
            rawName, nameof(RecipeTemplate.Name), EntityFieldConsts.NameMinLength, RecipeTemplateConsts.NameMaxLength);
    }

    private async Task EnsureNameUniqueAsync(Guid companyId, string normalizedName, Guid excludeId)
    {
        var duplicate = await AsyncExecuter.AnyAsync(
            (await _repository.GetQueryableAsync())
                .Where(x => x.CompanyId == companyId && x.Id != excludeId && x.Name == normalizedName));

        if (duplicate)
        {
            throw new BusinessException("TradeXpress:RecipeTemplate:NameAlreadyExists").WithData("name", normalizedName);
        }
    }

    /// <summary>Mapperly eşlemesi + TÜRETİLMİŞ alan (satır sayısı entity'de kolon değil).</summary>
    private RecipeTemplateListDto ToListDto(RecipeTemplate entity)
    {
        var dto = ObjectMapper.Map<RecipeTemplate, RecipeTemplateListDto>(entity);
        dto.LineCount = entity.Lines.Count;
        return dto;
    }
}
