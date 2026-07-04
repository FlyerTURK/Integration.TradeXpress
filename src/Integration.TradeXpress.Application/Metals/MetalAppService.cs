using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.TradeXpress.Commodities;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.Metals;

/// <summary>
/// Metal (Maden) CRUD. FollowingUnit ZORUNLU; Factor &gt;0 (üst sınır yok). Görünürlük/guard/liste/picker davranışı
/// <see cref="FollowingUnitCatalogAppService{TEntity,TGetDto,TListDto,TListRequest,TCreateInput,TUpdateInput}"/> tabanından
/// (host kataloğu + tenant kendi kayıtları; picker birim düzeni → Factor desc → Code asc).
/// </summary>
[Authorize]
public class MetalAppService
    : FollowingUnitCatalogAppService<Metal, MetalGetDto, MetalListDto, MetalListRequestDto, MetalCreateDto, MetalUpdateDto>,
      IMetalAppService
{
    public MetalAppService(
        IRepository<Metal, Guid> repository,
        IRepository<CurrencyUnit, Guid> unitRepository)
        : base(repository, unitRepository)
    {
        // Katalog yönetimi izinli (okuma/liste serbest — [Authorize] yeter): combo ✎/+ görünürlüğüyle hizalı.
        CreatePolicyName = TradeXpressPermissions.Metals.Create;
        UpdatePolicyName = TradeXpressPermissions.Metals.Update;
        DeletePolicyName = TradeXpressPermissions.Metals.Delete;
    }

    protected override ISet<string> AllowedListFields { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "IsActive", "Id" };

    protected override string EditGlobalErrorCode
    {
        get { return "TradeXpress:Metal:CannotEditGlobalAsTenant"; }
    }

    protected override string DeleteGlobalErrorCode
    {
        get { return "TradeXpress:Metal:CannotDeleteGlobalAsTenant"; }
    }

    protected override Expression<Func<Metal, string>> PickerOrderSelector
    {
        get { return x => x.Code; }   // kullanılmaz — picker composite sırayı tabandaki override kurar
    }

    protected override Guid FollowingUnitIdOf(Metal entity)
    {
        return entity.FollowingUnitId;
    }

    protected override decimal CompositeFactorOf(Metal entity)
    {
        return entity.Factor;
    }

    protected override string CodeOf(Metal entity)
    {
        return entity.Code;
    }

    protected override Task<Metal> MapToEntityAsync(MetalCreateDto createInput)
    {
        // TenantId otomatik (host→null, tenant→kendi); zengin ctor + SetX.
        var entity = new Metal(
            createInput.Code, createInput.Name, createInput.FollowingUnitId!.Value,
            createInput.Factor, createInput.FactorChange,
            createInput.IsQuantity, createInput.StableQuantity,
            createInput.LaborType, createInput.LaborTypeChange,
            createInput.EntryLabor, createInput.EntryLaborUnitId, createInput.EntryLaborChange,
            createInput.ExitLabor, createInput.ExitLaborUnitId, createInput.ExitLaborChange,
            createInput.CostUnitId);
        entity.SetBarcode(createInput.Barcode);
        entity.SetDescription(createInput.Description);
        return Task.FromResult(entity);
    }

    protected override Task EnsureCreateCodeUniqueAsync(Metal entity)
    {
        // Update ile aynı scope/error-code (TenantId bacağı standart filter'dan): aynı kod → dostane hata.
        return EnsureCodeUniqueAsync(
            entity, x => x.Code == entity.Code, "TradeXpress:Metal:CodeAlreadyExists", excludeSelf: false);
    }

    protected override async Task MapToEntityAsync(MetalUpdateDto updateInput, Metal entity)
    {
        // Kod düzenlenebilir (ürün kuralı 2026-07-04); benzersizlik scope'u DB unique index (TenantId, Code) ile hizalı.
        await ApplyCodeChangeAsync(
            entity,
            updateInput.Code,
            raw => StringFieldGuard.NormalizeCode(
                raw, nameof(Metal.Code), EntityFieldConsts.CodeMinLength, MetalConsts.CodeMaxLength),
            e => e.Code,
            (e, code) => e.SetCode(code),
            code => x => x.Code == code,
            "TradeXpress:Metal:CodeAlreadyExists");

        entity.SetName(updateInput.Name);
        entity.SetFollowingUnit(updateInput.FollowingUnitId!.Value);
        entity.SetFactor(updateInput.Factor);
        entity.SetFactorChange(updateInput.FactorChange);
        entity.SetQuantityTracking(updateInput.IsQuantity, updateInput.StableQuantity);
        entity.SetLabor(
            updateInput.LaborType, updateInput.LaborTypeChange,
            updateInput.EntryLabor, updateInput.EntryLaborUnitId, updateInput.EntryLaborChange,
            updateInput.ExitLabor, updateInput.ExitLaborUnitId, updateInput.ExitLaborChange,
            updateInput.CostUnitId);
        entity.SetBarcode(updateInput.Barcode);
        entity.SetDescription(updateInput.Description);
        entity.SetActive(updateInput.IsActive);
    }
}
