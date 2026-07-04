using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.Accounts;

/// <summary>
/// SubAccount CRUD — <b>per-tenant + branch-scoped</b>, bir <see cref="Account"/>'a bağlı. Liste
/// <see cref="SubAccountListRequestDto.AccountId"/> ve/veya <see cref="SubAccountListRequestDto.BranchId"/>
/// ile daraltılır. Parent hesap ve şube oluşturmada doğrulanır; sonradan değişmez.
/// </summary>
[Authorize(TradeXpressPermissions.SubAccounts.Default)]
public class SubAccountAppService : TradeXpressAppService, ISubAccountAppService
{
    private readonly IRepository<SubAccount, Guid> _repository;
    private readonly IRepository<Account, Guid> _accountRepository;
    private readonly IRepository<Branch, Guid> _branchRepository;
    private readonly ICurrentCompany _currentCompany;

    private static readonly HashSet<string> AllowedListFields =
        new(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "AccountCode", "BranchCode", "IsActive", "Id", "AccountId", "BranchId" };

    public SubAccountAppService(
        IRepository<SubAccount, Guid> repository,
        IRepository<Account, Guid> accountRepository,
        IRepository<Branch, Guid> branchRepository,
        ICurrentCompany currentCompany)
    {
        _repository = repository;
        _accountRepository = accountRepository;
        _branchRepository = branchRepository;
        _currentCompany = currentCompany;
    }

    public virtual async Task<PagedResultDto<SubAccountListDto>> GetListAsync(SubAccountListRequestDto input)
    {
        // ŞİRKET SCOPE — sunucu zorlar (client CompanyId GÖNDERMEZ; sızıntı önlemi, [[scoped-yetki-tasarimi]]).
        // Aktif çalışma şirketi yoksa boş döner (host/API bağlamı). Account.CompanyId üzerinden inner-join ile daraltılır.
        if (_currentCompany.Id is not { } companyId)
            return new PagedResultDto<SubAccountListDto>(0, new List<SubAccountListDto>());

        var accounts = (await _accountRepository.GetQueryableAsync()).Where(a => a.CompanyId == companyId);
        var branches = await _branchRepository.GetQueryableAsync();
        var query = await _repository.GetQueryableAsync();

        if (input.AccountId.HasValue)
            query = query.Where(s => s.AccountId == input.AccountId.Value);

        // ŞUBE filtresi: BranchId verilirse → company-level (BranchId=null) + o şubeye özel olanlar;
        // verilmezse şube daraltması YOK (tüm şirket — liste sayfaları için). Combo working branch'i geçer.
        if (input.BranchId.HasValue)
            query = query.Where(s => s.BranchId == null || s.BranchId == input.BranchId.Value);

        // Branch OPSİYONEL → korelasyonlu alt-sorgu (left-join etkisi): null şubeli alt hesaplar da listede kalır.
        var rows = query
            .Join(accounts, s => s.AccountId, a => a.Id, (s, a) => new SubAccountListRow
            {
                Id = s.Id,
                AccountId = s.AccountId,
                AccountCode = a.Code,
                AccountName = a.Name,
                BranchId = s.BranchId,
                BranchCode = s.BranchId == null
                    ? null
                    : branches.Where(b => b.Id == s.BranchId).Select(b => b.Code).FirstOrDefault(),
                Code = s.Code,
                Name = s.Name,
                IsActive = s.IsActive,
            })
            .ApplyListRequest(input, AllowedListFields);

        var totalCount = await AsyncExecuter.CountAsync(rows);
        var items = await AsyncExecuter.ToListAsync(rows.Skip(input.SkipCount).Take(input.MaxResultCount));

        return new PagedResultDto<SubAccountListDto>(
            totalCount,
            items.Select(r => new SubAccountListDto
            {
                Id = r.Id,
                AccountId = r.AccountId,
                AccountCode = r.AccountCode,
                AccountName = r.AccountName,
                BranchId = r.BranchId,
                BranchCode = r.BranchCode,
                Code = r.Code,
                Name = r.Name,
                IsActive = r.IsActive,
            }).ToList());
    }

    public virtual async Task<SubAccountGetDto> GetAsync(Guid id) => await ToGetDtoAsync(await _repository.GetAsync(id));

    [Authorize(TradeXpressPermissions.SubAccounts.Create)]
    public virtual async Task<SubAccountGetDto> CreateAsync(SubAccountCreateDto input)
    {
        if (CurrentTenant.Id == null)
            throw new BusinessException("TradeXpress:Company:HostHasNoCompanies");

        // Güvenlik sınırı: CompanyId client'tan DEĞİL, görünür parent hesaptan DENORMALİZE edilir
        // (Account'un kendisi company query-filter altında → yabancı şirketin hesabı görünmez → türetme sızmaz).
        var account = await EnsureAccountVisibleAsync(input.AccountId);
        var branchId = await ResolveBranchAsync(input.BranchId);   // opsiyonel — null geçerli

        var entity = new SubAccount(account.CompanyId, account.Id, branchId, input.Code, input.Name);
        entity.SetDescription(input.Description);

        await _repository.InsertAsync(entity, autoSave: true);
        return await ToGetDtoAsync(entity);
    }

    [Authorize(TradeXpressPermissions.SubAccounts.Update)]
    public virtual async Task<SubAccountGetDto> UpdateAsync(Guid id, SubAccountUpdateDto input)
    {
        var entity = await _repository.GetAsync(id);

        entity.SetName(input.Name);
        entity.SetDescription(input.Description);
        entity.SetActive(input.IsActive);

        await _repository.UpdateAsync(entity, autoSave: true);
        return await ToGetDtoAsync(entity);
    }

    [Authorize(TradeXpressPermissions.SubAccounts.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        // Güvenlik sınırı (Account/Vault deseniyle hizalı): ÖNCE görünür kaydı yükle → yabancı şirketin
        // alt hesabı company query-filter altında gizli → EntityNotFound (fail-loud). ABP'nin DeleteAsync(id)
        // içi FindAsync ile SESSİZCE no-op ederdi; sınırı açık/tutarlı kılmak için GetAsync ile yüklüyoruz.
        var entity = await _repository.GetAsync(id);
        await _repository.DeleteAsync(entity, autoSave: true);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<Account> EnsureAccountVisibleAsync(Guid? accountId)
    {
        if (accountId is not { } id || id == Guid.Empty || await _accountRepository.FindAsync(id) is not { } account)
            throw new EntityNotFoundException(typeof(Account), accountId ?? Guid.Empty);
        return account;
    }

    /// <summary>Şube OPSİYONEL: null/boş → null döner; verilmişse görünür olmalı (yoksa EntityNotFound).</summary>
    private async Task<Guid?> ResolveBranchAsync(Guid? branchId)
    {
        if (branchId is not { } id || id == Guid.Empty)
            return null;
        if (await _branchRepository.FindAsync(id) == null)
            throw new EntityNotFoundException(typeof(Branch), id);
        return id;
    }

    private async Task<SubAccountGetDto> ToGetDtoAsync(SubAccount s)
    {
        var accountCode = await AsyncExecuter.FirstOrDefaultAsync(
            (await _accountRepository.GetQueryableAsync()).Where(a => a.Id == s.AccountId).Select(a => a.Code));

        string? branchCode = null;
        if (s.BranchId is { } bid)
            branchCode = await AsyncExecuter.FirstOrDefaultAsync(
                (await _branchRepository.GetQueryableAsync()).Where(b => b.Id == bid).Select(b => b.Code));

        return new SubAccountGetDto
        {
            Id = s.Id,
            AccountId = s.AccountId,
            AccountCode = accountCode ?? string.Empty,
            BranchId = s.BranchId,
            BranchCode = branchCode,
            Code = s.Code,
            Name = s.Name,
            Description = s.Description,
            IsActive = s.IsActive,
        };
    }

    private sealed class SubAccountListRow
    {
        public Guid Id { get; set; }
        public Guid AccountId { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public Guid? BranchId { get; set; }
        public string? BranchCode { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }
}
