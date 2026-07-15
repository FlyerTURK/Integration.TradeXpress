using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Authorization;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Vaults;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Volo.Abp.Users;

namespace Integration.TradeXpress.Confirmations;

/// <summary>
/// Teyit (organizasyon-içi karşılıklı ayna onayı) servisi. Karşı taraf bir İÇ KASA olduğunda process HEMEN
/// postlanmaz: <see cref="ProposeAsync"/> Teyit kaydı doğurur (Proposed, postlama YOK) → alıcı
/// <see cref="DeclareAsync"/> ile KENDİ satırını KENDİ ELİYLE yazar (sistem AYNALAMAZ; sunucu yalnız iki
/// satırın AYNA olduğunu doğrular) → gönderen <see cref="ConfirmAsync"/> ile teyit edince iki bacak TEK
/// transaction'da atomik postlanır.
///
/// <para><b>Generic (process-agnostik):</b> her taraf kendi TAM <see cref="VoucherLineDto"/>'sunu yazar,
/// payload olarak saklanır ve teyitte <see cref="ConfirmationVoucherMaterializer"/> ile o tipin KENDİ poster'ı
/// üzerinden materyalize edilir. Nakit'e özel dal YOKTUR; desteklenen tipler
/// <see cref="ConfirmationProcessPolicy"/>'dedir (SSOT — UI aynı kuralı okur).</para>
///
/// <para><b>Zero-trust:</b> tek taraflı beyan ötekinin defterini kımıldatmaz — Confirmed öncesi ledger'a
/// HİÇBİR ŞEY yazılmaz. Ayna TAM tutmalıdır (emtia+varyant+miktar+tutar+ana/karşılık birimi+karşılık tutarı,
/// ZIT yön); tutmazsa teyit açılmaz, fark yüzeye çıkar (fire/kayıp dedektörü).
/// <b>İPTAL YOKTUR:</b> gönderen teklifi geri çekemez (sorumluluk onda); süreci yalnız alıcı
/// <see cref="RejectAsync"/> ile durdurur.</para>
///
/// <para><b>Yetki ekseni</b> (her aksiyon kendi tarafının kasasını ister): Propose/Confirm = BAŞLATAN kasa,
/// Declare/Reject = KARŞI kasa. Materyalizasyon yetki İDDİA ETMEZ — yetki authoring anında alınmıştır
/// (gerekçe: <see cref="ConfirmationVoucherMaterializer"/>).</para>
/// </summary>
[Authorize]
public class ConfirmationAppService : TradeXpressAppService, IConfirmationAppService
{
    private readonly IRepository<Confirmation, Guid> _confirmationRepository;
    private readonly IRepository<Vault, Guid> _vaultRepository;
    private readonly IRepository<CurrencyUnit, Guid> _currencyUnitRepository;
    private readonly IRepository<Company, Guid> _companyRepository;
    private readonly ICurrentCompany _currentCompany;
    private readonly IScopedGrantResolver _scopedGrantResolver;
    private readonly ConfirmationVoucherMaterializer _materializer;
    private readonly IDataFilter _dataFilter;

    public ConfirmationAppService(
        IRepository<Confirmation, Guid> confirmationRepository,
        IRepository<Vault, Guid> vaultRepository,
        IRepository<CurrencyUnit, Guid> currencyUnitRepository,
        IRepository<Company, Guid> companyRepository,
        ICurrentCompany currentCompany,
        IScopedGrantResolver scopedGrantResolver,
        ConfirmationVoucherMaterializer materializer,
        IDataFilter dataFilter)
    {
        _confirmationRepository = confirmationRepository;
        _vaultRepository        = vaultRepository;
        _currencyUnitRepository = currencyUnitRepository;
        _companyRepository      = companyRepository;
        _currentCompany         = currentCompany;
        _scopedGrantResolver    = scopedGrantResolver;
        _materializer           = materializer;
        _dataFilter             = dataFilter;
    }

    /// <summary>TEKLİF: gönderen KENDİ satırını yazar; karşı taraf aynı şirkette bir İÇ KASA olmalıdır.
    /// Teyit Proposed doğar, alıcının GELEN'ine düşer. <b>POSTLAMA YOK</b> — ledger kımıldamaz.
    /// Yetki = BAŞLATAN kasa (kimse başkasının kasasından teklif açamaz) + o process tipinin kendi izni.</summary>
    [Authorize(TradeXpressPermissions.Confirmations.Propose)]
    public virtual async Task<ConfirmationDto> ProposeAsync(ProposeConfirmationInput input)
    {
        var companyId = EnsureCurrentCompanyId();

        var initiator = await GetVaultInCompanyAsync(companyId, input.InitiatorVaultId);
        // Karşı taraf İÇ kasa olmalı: yoksa/başka şirketteyse Teyit'in konusu değildir (dış taraf = normal process).
        var counterparty = await GetInternalCounterpartyVaultAsync(companyId, input.CounterpartyVaultId);

        await EnsureVaultAuthorizedAsync(companyId, initiator, "TradeXpress:Confirmation:NotAuthorizedForInitiatorVault");
        await EnsureProcessAllowedAsync(input.Line.Type);

        var confirmation = new Confirmation(
            companyId,
            initiator.Id,
            counterparty.Id,
            ToMirrorKey(input.Line),
            ConfirmationPayloadSerializer.Serialize(input.Line),
            input.Note);
        await _confirmationRepository.InsertAsync(confirmation, autoSave: true);

        return await MapToDtoAsync(confirmation);
    }

    /// <summary>BEYAN: alıcı KENDİ ELİYLE kendi satırını yazar (sistem aynalamaz) — input onun KENDİ gözlediği
    /// process satırıdır. Sunucu bunun gönderenin satırıyla AYNA olduğunu doğrular; tutmazsa Teyit AÇILMAZ
    /// (uyuşmazlık yüzeye çıkar). <b>POSTLAMA YOK.</b> Yetki = KARŞI kasa.</summary>
    [Authorize(TradeXpressPermissions.Confirmations.Declare)]
    public virtual async Task<ConfirmationDto> DeclareAsync(DeclareConfirmationInput input)
    {
        var companyId    = EnsureCurrentCompanyId();
        var confirmation = await GetOwnedConfirmationAsync(input.Id);

        var counterparty = await _vaultRepository.GetAsync(confirmation.CounterpartyVaultId);
        await EnsureVaultAuthorizedAsync(companyId, counterparty, "TradeXpress:Confirmation:NotAuthorizedForCounterpartyVault");
        await EnsureProcessAllowedAsync(input.Line.Type);

        EnsureMirrors(confirmation, input.Line);

        confirmation.Declare(ConfirmationPayloadSerializer.Serialize(input.Line), input.Note);
        await _confirmationRepository.UpdateAsync(confirmation, autoSave: true);

        return await MapToDtoAsync(confirmation);
    }

    /// <summary>TEYİT: gönderen alıcının kaydını teyit eder → iki ayna bacak AYNI transaction'da atomik
    /// postlanır. Her bacak, o tarafın KENDİ payload'undan, o process tipinin KENDİ poster'ı üzerinden
    /// materyalize edilir; fiş başlığı KARŞI kasanın vault-cari'sidir → karşılıklı borç/alacak kendiliğinden
    /// doğar. Gerçekleşen fişler Teyit'e iliştirilir. Yetki = BAŞLATAN kasa.</summary>
    [Authorize(TradeXpressPermissions.Confirmations.Confirm)]
    [UnitOfWork(isTransactional: true)]
    public virtual async Task<ConfirmationDto> ConfirmAsync(ConfirmConfirmationInput input)
    {
        var companyId    = EnsureCurrentCompanyId();
        var confirmation = await GetOwnedConfirmationAsync(input.Id);

        var initiator    = await _vaultRepository.GetAsync(confirmation.InitiatorVaultId);
        var counterparty = await _vaultRepository.GetAsync(confirmation.CounterpartyVaultId);

        await EnsureVaultAuthorizedAsync(companyId, initiator, "TradeXpress:Confirmation:NotAuthorizedForInitiatorVault");

        // Durum guard'ı POSTLAMADAN ÖNCE: entity.Confirm de zorlar ama o ancak fişler yazıldıktan sonra çağrılır.
        EnsureDeclared(confirmation);

        var company = await _companyRepository.GetAsync(companyId);

        var initiatorLine    = ConfirmationPayloadSerializer.Deserialize(confirmation.InitiatorPayloadJson);
        var counterpartyLine = ConfirmationPayloadSerializer.Deserialize(confirmation.CounterpartyPayloadJson);

        // Kasa sistem carileri (idempotent lazy garanti) — her bacağın BAŞLIĞI KARŞI tarafın carisidir:
        // A'nın satırı cari(B)'ye, B'nin satırı cari(A)'ya düşer → karşılıklı borç/alacak kendiliğinden doğar.
        var initiatorCari    = await _materializer.EnsureVaultCurrentAccountAsync(company, initiator);
        var counterpartyCari = await _materializer.EnsureVaultCurrentAccountAsync(company, counterparty);

        var initiatorVoucher    = await _materializer.MaterializeAsync(company, initiator, counterpartyCari, initiatorLine);
        var counterpartyVoucher = await _materializer.MaterializeAsync(company, counterparty, initiatorCari, counterpartyLine);

        confirmation.Confirm(initiatorVoucher.Id, counterpartyVoucher.Id, input.Note);
        await _confirmationRepository.UpdateAsync(confirmation, autoSave: true);

        return await MapToDtoAsync(confirmation);
    }

    /// <summary>RED: alıcı kabul etmez → durum kapanır (postlanmış bacak yok). Süreci durdurmanın TEK yolu —
    /// gönderenin iptali yoktur. Yetki = KARŞI kasa.</summary>
    [Authorize(TradeXpressPermissions.Confirmations.Reject)]
    public virtual async Task<ConfirmationDto> RejectAsync(RejectConfirmationInput input)
    {
        var companyId    = EnsureCurrentCompanyId();
        var confirmation = await GetOwnedConfirmationAsync(input.Id);

        var counterparty = await _vaultRepository.GetAsync(confirmation.CounterpartyVaultId);
        await EnsureVaultAuthorizedAsync(companyId, counterparty, "TradeXpress:Confirmation:NotAuthorizedForCounterpartyVault");

        confirmation.Reject(input.Reason);
        await _confirmationRepository.UpdateAsync(confirmation, autoSave: true);

        return await MapToDtoAsync(confirmation);
    }

    /// <summary>Gelen/Giden kutusu: kullanıcının BAŞLATAN (giden) ya da KARŞI (gelen) tarafta olduğu teyitler.
    /// TÜM statüler (reddedilen/kapanan gizlenmez — izi sürülsün). Company-scoped; opsiyonel
    /// <see cref="ConfirmationListRequest.VaultId"/> ile tek kasaya daraltılır.</summary>
    [Authorize(TradeXpressPermissions.Confirmations.View)]
    public virtual async Task<List<ConfirmationDto>> GetListAsync(ConfirmationListRequest input)
    {
        var companyId = EnsureCurrentCompanyId();
        var access    = await _scopedGrantResolver.ResolveAsync(CurrentUser.GetId());

        var query = (await _confirmationRepository.GetQueryableAsync())
            .Where(c => c.CompanyId == companyId);

        if (input.VaultId is { } vaultId)
        {
            query = query.Where(c => c.InitiatorVaultId == vaultId || c.CounterpartyVaultId == vaultId);
        }

        var confirmations = await AsyncExecuter.ToListAsync(query.OrderByDescending(c => c.CreationTime));
        if (confirmations.Count == 0)
        {
            return new List<ConfirmationDto>();
        }

        // Kasa → şube/kod eşlemesi (CanAccessVault şube ister; DTO kasa kodu gösterir) — tek sorguda topla.
        var vaultIds = confirmations
            .SelectMany(c => new[] { c.InitiatorVaultId, c.CounterpartyVaultId })
            .Distinct()
            .ToList();
        var vaults = await AsyncExecuter.ToListAsync(
            (await _vaultRepository.GetQueryableAsync()).Where(v => vaultIds.Contains(v.Id)));
        var vaultBranch = vaults.ToDictionary(v => v.Id, v => v.BranchId);
        var vaultCode   = vaults.ToDictionary(v => v.Id, v => v.Code);

        // İki-taraflı görünürlük: başlatan VEYA karşı kasaya erişim (giden kutusu + gelen kutusu).
        bool CanAccess(Guid vId)
        {
            return vaultBranch.TryGetValue(vId, out var branchId)
                && access.CanAccessVault(companyId, branchId, vId);
        }

        var visible = confirmations
            .Where(c => CanAccess(c.InitiatorVaultId) || CanAccess(c.CounterpartyVaultId))
            .ToList();

        var unitCodes = await ResolveUnitCodeMapAsync(
            visible.SelectMany(c => new[] { c.MainUnitId ?? Guid.Empty, c.PayUnitId ?? Guid.Empty }));

        // UI-gating bayrakları: SSOT — client yetkiyi kendi türetmesin diye sunucudaki CanAccess sonucu DTO'ya
        // yansır. Yalnız buton görünürlüğü içindir; aksiyon çağrısında sunucu yetkiyi TEKRAR enforce eder.
        return visible
            .Select(c => ToDto(c, vaultCode, unitCodes, CanAccess(c.InitiatorVaultId), CanAccess(c.CounterpartyVaultId)))
            .ToList();
    }

    /// <summary>Bir tarafın KENDİ eliyle yazdığı satır (denetim / uyuşmazlık incelemesi). Görünürlük = o teyidin
    /// HERHANGİ bir tarafına erişim. Ön-doldurma için KULLANILMAZ (bkz. arayüz notu).</summary>
    [Authorize(TradeXpressPermissions.Confirmations.View)]
    public virtual async Task<VoucherLineDto?> GetPayloadAsync(Guid id, bool initiatorSide)
    {
        var companyId    = EnsureCurrentCompanyId();
        var confirmation = await GetOwnedConfirmationAsync(id);

        var initiator    = await _vaultRepository.GetAsync(confirmation.InitiatorVaultId);
        var counterparty = await _vaultRepository.GetAsync(confirmation.CounterpartyVaultId);
        var access       = await _scopedGrantResolver.ResolveAsync(CurrentUser.GetId());
        if (!access.CanAccessVault(companyId, initiator.BranchId, initiator.Id)
            && !access.CanAccessVault(companyId, counterparty.BranchId, counterparty.Id))
        {
            throw new EntityNotFoundException(typeof(Confirmation), id);
        }

        var payload = initiatorSide ? confirmation.InitiatorPayloadJson : confirmation.CounterpartyPayloadJson;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;   // alıcı henüz kendi satırını yazmadı (Proposed)
        }

        return ConfirmationPayloadSerializer.Deserialize(payload);
    }

    // ── ayna doğrulama (zero-trust çekirdeği) ────────────────────────────────────

    /// <summary>Satırdan AYNA ANAHTARI türetir — client anahtarı ayrıca göndermez (çift kaynak = sapma riski).
    /// <para><c>MainUnitId</c> NORMALİZE edilir (<c>Guid.Empty</c> → null): satır DTO'sunda ana birim
    /// non-nullable'dır ve ana bacağı olmayan tipler (Dekont) oraya <c>Guid.Empty</c> yazar. "Boş"un tek
    /// gösterimi olmazsa iki taraf aynı şeyi farklı kodlayıp ayna yanlış kırılabilir.</para></summary>
    private static ConfirmationMirrorKey ToMirrorKey(VoucherLineDto line)
    {
        return new ConfirmationMirrorKey(
            line.Type,
            line.Direction,
            line.CommodityId,
            line.VariantId,
            line.Quantity,
            line.Amount,
            line.MainUnitId == Guid.Empty ? null : line.MainUnitId,
            line.PayUnitId,
            line.PayTotal);
    }

    /// <summary>TAM AYNA kriteri (spec §3): emtia + varyant + miktar + tutar + ana birim + karşılık birimi +
    /// karşılık tutarı AYNI, yön ZIT. Tutmazsa Teyit AÇILMAZ — beklenen/gelen değerler hataya iliştirilir ki
    /// fark (fire/kayıp) adresli teşhir edilsin.</summary>
    private static void EnsureMirrors(Confirmation confirmation, VoucherLineDto line)
    {
        var expected = confirmation.ToMirrorKey().Mirrored();
        var actual   = ToMirrorKey(line);
        if (expected == actual)
        {
            return;
        }

        throw new BusinessException("TradeXpress:Confirmation:MirrorMismatch")
            .WithData("expectedProcessType", expected.Type)
            .WithData("actualProcessType", actual.Type)
            .WithData("expectedCommodityId", expected.CommodityId)
            .WithData("actualCommodityId", actual.CommodityId)
            .WithData("expectedVariantId", expected.VariantId)
            .WithData("actualVariantId", actual.VariantId)
            .WithData("expectedQuantity", expected.Quantity)
            .WithData("actualQuantity", actual.Quantity)
            .WithData("expectedAmount", expected.Amount)
            .WithData("actualAmount", actual.Amount)
            .WithData("expectedMainUnitId", expected.MainUnitId)
            .WithData("actualMainUnitId", actual.MainUnitId)
            .WithData("expectedPayUnitId", expected.PayUnitId)
            .WithData("actualPayUnitId", actual.PayUnitId)
            .WithData("expectedPayTotal", expected.PayTotal)
            .WithData("actualPayTotal", actual.PayTotal)
            .WithData("expectedDirection", expected.Direction)
            .WithData("actualDirection", actual.Direction);
    }

    // ── guard'lar / çözümleyiciler ───────────────────────────────────────────────

    /// <summary>Sızıntı önleme: CompanyId DAİMA working-context'ten zorlanır (client'a güvenilmez).</summary>
    private Guid EnsureCurrentCompanyId()
    {
        if (_currentCompany.Id is not { } companyId)
        {
            throw new BusinessException("TradeXpress:Confirmation:CompanyContextRequired");
        }

        return companyId;
    }

    /// <summary>İç kip guard'ı: tip <see cref="ConfirmationProcessPolicy"/>'de AÇIK olmalı (UI gate'i bypass eden
    /// doğrudan API çağrısına karşı) + kullanıcı o process tipinin KENDİ iznine sahip olmalı — Teyit, normal
    /// yolda yazamayacağı bir satırı yazmanın arka kapısı OLAMAZ (ProcessTypePermissionMap tek kaynak).</summary>
    private async Task EnsureProcessAllowedAsync(ProcessType type)
    {
        if (!ConfirmationProcessPolicy.IsInternalModeSupported(type))
        {
            throw new BusinessException("TradeXpress:Confirmation:ProcessTypeNotSupported")
                .WithData("processType", type);
        }

        await AuthorizationService.CheckAsync(ProcessTypePermissionMap.PermissionFor(type));
    }

    /// <summary>Teyit'i yükler + working şirkete aitliğini doğrular (yabancı şirket = yokmuş gibi davran).</summary>
    private async Task<Confirmation> GetOwnedConfirmationAsync(Guid id)
    {
        var confirmation = await _confirmationRepository.GetAsync(id);
        if (confirmation.CompanyId != EnsureCurrentCompanyId())
        {
            throw new EntityNotFoundException(typeof(Confirmation), id);
        }

        return confirmation;
    }

    /// <summary>Kasayı yükler + working şirkete YAPISAL aitliğini doğrular (client başka şirketin kasasını gönderemez).</summary>
    private async Task<Vault> GetVaultInCompanyAsync(Guid companyId, Guid vaultId)
    {
        var vault = await _vaultRepository.FindAsync(vaultId);
        if (vault == null || vault.CompanyId != companyId)
        {
            throw new BusinessException("TradeXpress:Confirmation:VaultNotInCompany");
        }

        return vault;
    }

    /// <summary>Teyit'in ön şartı: karşı taraf AYNI ŞİRKETTE bir iç kasa olmalı. Değilse ayna onayının öznesi
    /// yoktur (dış taraf kendi eliyle kayıt yazamaz) → process normal yolundan işler.</summary>
    private async Task<Vault> GetInternalCounterpartyVaultAsync(Guid companyId, Guid counterpartyVaultId)
    {
        var vault = await _vaultRepository.FindAsync(counterpartyVaultId);
        if (vault == null || vault.CompanyId != companyId)
        {
            throw new BusinessException("TradeXpress:Confirmation:CounterpartyMustBeInternalVault");
        }

        return vault;
    }

    /// <summary>Kullanıcının verilen kasaya erişim GRANT'ını doğrular (working-context scope; en-spesifik-kazanır).</summary>
    private async Task EnsureVaultAuthorizedAsync(Guid companyId, Vault vault, string errorCode)
    {
        var access = await _scopedGrantResolver.ResolveAsync(CurrentUser.GetId());
        if (!access.CanAccessVault(companyId, vault.BranchId, vault.Id))
        {
            throw new BusinessException(errorCode);
        }
    }

    /// <summary>Postlama guard'ı: Teyit yalnız Declared'dan teyitlenebilir (alıcı kendi kaydını yazmadan
    /// gönderenin tek taraflı beyanı postlanamaz — zero-trust).</summary>
    private static void EnsureDeclared(Confirmation confirmation)
    {
        if (confirmation.Status != ConfirmationStatus.Declared)
        {
            throw new BusinessException("TradeXpress:Confirmation:InvalidStateTransition")
                .WithData("current", confirmation.Status)
                .WithData("expected", ConfirmationStatus.Declared);
        }
    }

    private async Task<Dictionary<Guid, string>> ResolveUnitCodeMapAsync(IEnumerable<Guid> unitIds)
    {
        var ids = unitIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        // Host-global birimler tenant altında IMultiTenant filtresiyle gizlenir → kod çözümü için filtre kapalı.
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var units = await AsyncExecuter.ToListAsync(
                (await _currencyUnitRepository.GetQueryableAsync()).Where(u => ids.Contains(u.Id)));
            return units.ToDictionary(u => u.Id, u => u.Code);
        }
    }

    private async Task<ConfirmationDto> MapToDtoAsync(Confirmation confirmation)
    {
        var vaultIds = new List<Guid> { confirmation.InitiatorVaultId, confirmation.CounterpartyVaultId };
        var vaults = await AsyncExecuter.ToListAsync(
            (await _vaultRepository.GetQueryableAsync()).Where(v => vaultIds.Contains(v.Id)));
        var vaultCode = vaults.ToDictionary(v => v.Id, v => v.Code);
        var unitCodes = await ResolveUnitCodeMapAsync(
            new[] { confirmation.MainUnitId ?? Guid.Empty, confirmation.PayUnitId ?? Guid.Empty });
        return ToDto(confirmation, vaultCode, unitCodes);
    }

    private ConfirmationDto ToDto(
        Confirmation confirmation,
        IReadOnlyDictionary<Guid, string> vaultCode,
        IReadOnlyDictionary<Guid, string> unitCode,
        bool isInitiatorMine = false,
        bool isCounterpartyMine = false)
    {
        var dto = ObjectMapper.Map<Confirmation, ConfirmationDto>(confirmation);
        dto.InitiatorVaultCode    = vaultCode.GetValueOrDefault(confirmation.InitiatorVaultId);
        dto.CounterpartyVaultCode = vaultCode.GetValueOrDefault(confirmation.CounterpartyVaultId);
        dto.MainUnitCode          = confirmation.MainUnitId is { } mainUnitId
            ? unitCode.GetValueOrDefault(mainUnitId)
            : null;
        dto.PayUnitCode           = confirmation.PayUnitId is { } payUnitId
            ? unitCode.GetValueOrDefault(payUnitId)
            : null;
        // UI-gating bayrakları — yalnız GetListAsync doldurur; aksiyon dönüşlerinde (MapToDtoAsync) false kalır.
        dto.IsInitiatorMine    = isInitiatorMine;
        dto.IsCounterpartyMine = isCounterpartyMine;
        return dto;
    }
}
