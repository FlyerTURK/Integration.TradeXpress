using Riok.Mapperly.Abstractions;
using Volo.Abp.Mapperly;
using Integration.TradeXpress.Tenants;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Financials.Parities;
using Integration.TradeXpress.Cashes;
using Integration.TradeXpress.Services;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.N11Categories;
using Integration.TradeXpress.TrendyolCategories;
using Integration.TradeXpress.TrendyolBrands;
using Integration.TradeXpress.EtsyTaxonomies;
using Integration.TradeXpress.N11Cities;
using Integration.TradeXpress.N11Shipments;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.TrendyolProducts;
using Integration.TradeXpress.EtsyProducts;
using Integration.TradeXpress.Futures;
using Integration.TradeXpress.Scraps;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.Stones;
using Integration.TradeXpress.Jewelries;
using Integration.TradeXpress.Goods;
using Integration.TradeXpress.SpecialCodes;
using Integration.TradeXpress.Countries;
using Integration.TradeXpress.Geography;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.AssayOffices;
using Integration.TradeXpress.AddOns;
using Integration.TradeXpress.VariantTemplates;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.Scheduling;
using Integration.TradeXpress.Substitutions;
using Integration.TradeXpress.Orders;
using Integration.TradeXpress.Authorization;
using Integration.TradeXpress.Confirmations;
using Integration.TradeXpress.Attachments;
using Volo.Abp.TenantManagement;

namespace Integration.TradeXpress;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class TenantToTenantGetDtoMapper : MapperBase<Tenant, TenantGetDto>
{
    public override partial TenantGetDto Map(Tenant source);
    public override partial void Map(Tenant source, TenantGetDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class TenantToTenantListDtoMapper : MapperBase<Tenant, TenantListDto>
{
    public override partial TenantListDto Map(Tenant source);
    public override partial void Map(Tenant source, TenantListDto destination);
}

// ── CurrencyUnit ──────────────────────────────────────────────────────────────
// Margin VO'ları otomatik düzleştirilir (MarginOnBuy.Type → MarginOnBuyType).
// IsGlobal (TenantId==null) AppService'te elle set edilir.

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class CurrencyUnitToGetDtoMapper : MapperBase<CurrencyUnit, CurrencyUnitGetDto>
{
    [MapperIgnoreTarget(nameof(CurrencyUnitGetDto.IsGlobal))]
    [MapperIgnoreTarget(nameof(CurrencyUnitGetDto.IsSystem))]
    public override partial CurrencyUnitGetDto Map(CurrencyUnit source);
    public override partial void Map(CurrencyUnit source, CurrencyUnitGetDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class CurrencyUnitToListDtoMapper : MapperBase<CurrencyUnit, CurrencyUnitListDto>
{
    [MapperIgnoreTarget(nameof(CurrencyUnitListDto.IsGlobal))]
    [MapperIgnoreTarget(nameof(CurrencyUnitListDto.IsSystem))]
    public override partial CurrencyUnitListDto Map(CurrencyUnit source);
    public override partial void Map(CurrencyUnit source, CurrencyUnitListDto destination);
}

// ── Parity ──────────────────────────────────────────────────────────────────
// IsGlobal (TenantId==null), BaseCode/QuoteCode (FK→Code enrichment)
// AppService'te elle set edilir (entity'de karşılığı yok).

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class ParityToGetDtoMapper : MapperBase<Parity, ParityGetDto>
{
    [MapperIgnoreTarget(nameof(ParityGetDto.IsGlobal))]
    [MapperIgnoreTarget(nameof(ParityGetDto.IsSystem))]
    [MapperIgnoreTarget(nameof(ParityGetDto.BaseCode))]
    [MapperIgnoreTarget(nameof(ParityGetDto.QuoteCode))]
    public override partial ParityGetDto Map(Parity source);
    public override partial void Map(Parity source, ParityGetDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class ParityToListDtoMapper : MapperBase<Parity, ParityListDto>
{
    [MapperIgnoreTarget(nameof(ParityListDto.IsGlobal))]
    [MapperIgnoreTarget(nameof(ParityListDto.IsSystem))]
    [MapperIgnoreTarget(nameof(ParityListDto.BaseCode))]
    [MapperIgnoreTarget(nameof(ParityListDto.QuoteCode))]
    public override partial ParityListDto Map(Parity source);
    public override partial void Map(Parity source, ParityListDto destination);
}

// ── GetDto → Create/Update (PersistentCoordinator.CommitAsync; agnostic EntityEditForm save yolu) ──
// Coordinator IObjectMapper.Map<GetDto,Create/UpdateDto> çağırır; Mapperly bu eşlemeleri ister (fallback YOK).

[Mapper] public partial class CurrencyUnitGetToCreateMapper : MapperBase<CurrencyUnitGetDto, CurrencyUnitCreateDto>
{
    public override partial CurrencyUnitCreateDto Map(CurrencyUnitGetDto source);
    public override partial void Map(CurrencyUnitGetDto source, CurrencyUnitCreateDto destination);
}
[Mapper] public partial class CurrencyUnitGetToUpdateMapper : MapperBase<CurrencyUnitGetDto, CurrencyUnitUpdateDto>
{
    public override partial CurrencyUnitUpdateDto Map(CurrencyUnitGetDto source);
    public override partial void Map(CurrencyUnitGetDto source, CurrencyUnitUpdateDto destination);
}

[Mapper] public partial class CountryGetToCreateMapper : MapperBase<CountryGetDto, CountryCreateDto>
{
    public override partial CountryCreateDto Map(CountryGetDto source);
    public override partial void Map(CountryGetDto source, CountryCreateDto destination);
}
[Mapper] public partial class CountryGetToUpdateMapper : MapperBase<CountryGetDto, CountryUpdateDto>
{
    public override partial CountryUpdateDto Map(CountryGetDto source);
    public override partial void Map(CountryGetDto source, CountryUpdateDto destination);
}

[Mapper] public partial class CashGetToCreateMapper : MapperBase<CashGetDto, CashCreateDto>
{
    public override partial CashCreateDto Map(CashGetDto source);
    public override partial void Map(CashGetDto source, CashCreateDto destination);
}
[Mapper] public partial class CashGetToUpdateMapper : MapperBase<CashGetDto, CashUpdateDto>
{
    public override partial CashUpdateDto Map(CashGetDto source);
    public override partial void Map(CashGetDto source, CashUpdateDto destination);
}

[Mapper] public partial class AssayOfficeGetToCreateMapper : MapperBase<AssayOfficeGetDto, AssayOfficeCreateDto>
{
    public override partial AssayOfficeCreateDto Map(AssayOfficeGetDto source);
    public override partial void Map(AssayOfficeGetDto source, AssayOfficeCreateDto destination);
}
[Mapper] public partial class AssayOfficeGetToUpdateMapper : MapperBase<AssayOfficeGetDto, AssayOfficeUpdateDto>
{
    public override partial AssayOfficeUpdateDto Map(AssayOfficeGetDto source);
    public override partial void Map(AssayOfficeGetDto source, AssayOfficeUpdateDto destination);
}

// ── SchedulerAppointment ──────────────────────────────────────────────────────
// Entity↔DTO alan adları birebir → otomatik eşleme (CompanyId/TenantId/audit = source-only, hedefte yok).

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class SchedulerAppointmentToDtoMapper : MapperBase<SchedulerAppointment, SchedulerAppointmentDto>
{
    public override partial SchedulerAppointmentDto Map(SchedulerAppointment source);
    public override partial void Map(SchedulerAppointment source, SchedulerAppointmentDto destination);
}

// ── AssayOffice (entity→DTO: statik mapper anti-pattern'inden Mapperly'ye çevrildi) ──

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class AssayOfficeToGetDtoMapper : MapperBase<AssayOffice, AssayOfficeGetDto>
{
    public override partial AssayOfficeGetDto Map(AssayOffice source);
    public override partial void Map(AssayOffice source, AssayOfficeGetDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class AssayOfficeToListDtoMapper : MapperBase<AssayOffice, AssayOfficeListDto>
{
    public override partial AssayOfficeListDto Map(AssayOffice source);
    public override partial void Map(AssayOffice source, AssayOfficeListDto destination);
}

// ── AddOn (sipariş eklentisi katalogu) ──

[Mapper] public partial class AddOnGetToCreateMapper : MapperBase<AddOnGetDto, AddOnCreateDto>
{
    public override partial AddOnCreateDto Map(AddOnGetDto source);
    public override partial void Map(AddOnGetDto source, AddOnCreateDto destination);
}
[Mapper] public partial class AddOnGetToUpdateMapper : MapperBase<AddOnGetDto, AddOnUpdateDto>
{
    public override partial AddOnUpdateDto Map(AddOnGetDto source);
    public override partial void Map(AddOnGetDto source, AddOnUpdateDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class AddOnToGetDtoMapper : MapperBase<AddOn, AddOnGetDto>
{
    public override partial AddOnGetDto Map(AddOn source);
    public override partial void Map(AddOn source, AddOnGetDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class AddOnToListDtoMapper : MapperBase<AddOn, AddOnListDto>
{
    public override partial AddOnListDto Map(AddOn source);
    public override partial void Map(AddOn source, AddOnListDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class VariantTemplateToGetDtoMapper : MapperBase<VariantTemplate, VariantTemplateGetDto>
{
    public override partial VariantTemplateGetDto Map(VariantTemplate source);
    public override partial void Map(VariantTemplate source, VariantTemplateGetDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class VariantTemplateToListDtoMapper : MapperBase<VariantTemplate, VariantTemplateListDto>
{
    public override partial VariantTemplateListDto Map(VariantTemplate source);
    public override partial void Map(VariantTemplate source, VariantTemplateListDto destination);
}

// ── Service (statik mapper → Mapperly; IsGlobal = TenantId==null AppService'te elle set, CurrencyUnit deseni) ──

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class ServiceToGetDtoMapper : MapperBase<Service, ServiceGetDto>
{
    [MapperIgnoreTarget(nameof(ServiceGetDto.IsGlobal))]
    public override partial ServiceGetDto Map(Service source);
    public override partial void Map(Service source, ServiceGetDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class ServiceToListDtoMapper : MapperBase<Service, ServiceListDto>
{
    [MapperIgnoreTarget(nameof(ServiceListDto.IsGlobal))]
    public override partial ServiceListDto Map(Service source);
    public override partial void Map(Service source, ServiceListDto destination);
}

// ── SalesChannel (company-owned TPT). Somut alt-tip → tipe-özel GetDto (base alanlar + alt-tip credential'ları).
//    Polymorphic liste: base/alt-tip → SalesChannelListDto; ChannelType concrete tipten AppService'te set edilir
//    (mapper'da source yok → unmapped, [Mapper] default: RMG012 uyarısı ignore edilir). CompanyId/TenantId source-only. ──

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class SalesChannelTrN11ToGetDtoMapper : MapperBase<SalesChannelTrN11, SalesChannelTrN11GetDto>
{
    public override partial SalesChannelTrN11GetDto Map(SalesChannelTrN11 source);
    public override partial void Map(SalesChannelTrN11 source, SalesChannelTrN11GetDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class SalesChannelTrTrendyolToGetDtoMapper : MapperBase<SalesChannelTrTrendyol, SalesChannelTrTrendyolGetDto>
{
    // Token = yalnız-yazılır giriş alanı (base64(apiKey:apiSecret)); entity'de karşılığı YOK → çıkışta redakte edilir.
    [MapperIgnoreTarget(nameof(SalesChannelTrTrendyolGetDto.Token))]
    public override partial SalesChannelTrTrendyolGetDto Map(SalesChannelTrTrendyol source);
    public override partial void Map(SalesChannelTrTrendyol source, SalesChannelTrTrendyolGetDto destination);
}

[Mapper]
public partial class SalesChannelBaseToListDtoMapper : MapperBase<SalesChannelBase, SalesChannelListDto>
{
    public override partial SalesChannelListDto Map(SalesChannelBase source);
    public override partial void Map(SalesChannelBase source, SalesChannelListDto destination);
}

[Mapper]
public partial class SalesChannelTrN11ToListDtoMapper : MapperBase<SalesChannelTrN11, SalesChannelListDto>
{
    public override partial SalesChannelListDto Map(SalesChannelTrN11 source);
    public override partial void Map(SalesChannelTrN11 source, SalesChannelListDto destination);
}

[Mapper]
public partial class SalesChannelTrTrendyolToListDtoMapper : MapperBase<SalesChannelTrTrendyol, SalesChannelListDto>
{
    public override partial SalesChannelListDto Map(SalesChannelTrTrendyol source);
    public override partial void Map(SalesChannelTrTrendyol source, SalesChannelListDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class SalesChannelEtsyToGetDtoMapper : MapperBase<SalesChannelEtsy, SalesChannelEtsyGetDto>
{
    // IsConnected türetilmiş durum (refresh token dolu + süresi geçmemiş) — AppService clock'la hesaplar. Access/refresh
    // token'ları DTO'da HİÇ yok (sızıntı yüzeyi sıfır); SharedSecret map'lenir ama AppService çıkışta redakte eder.
    [MapperIgnoreTarget(nameof(SalesChannelEtsyGetDto.IsConnected))]
    public override partial SalesChannelEtsyGetDto Map(SalesChannelEtsy source);
    public override partial void Map(SalesChannelEtsy source, SalesChannelEtsyGetDto destination);
}

[Mapper]
public partial class SalesChannelEtsyToListDtoMapper : MapperBase<SalesChannelEtsy, SalesChannelListDto>
{
    public override partial SalesChannelListDto Map(SalesChannelEtsy source);
    public override partial void Map(SalesChannelEtsy source, SalesChannelListDto destination);
}

// ── N11 kategori (host-global taksonomi) → ağaç düğüm DTO'su ──
[Mapper]
public partial class N11CategoryToTreeNodeDtoMapper : MapperBase<N11Category, N11CategoryTreeNodeDto>
{
    public override partial N11CategoryTreeNodeDto Map(N11Category source);
    public override partial void Map(N11Category source, N11CategoryTreeNodeDto destination);
}

// ── Trendyol kategori (host-global taksonomi) → ağaç düğüm DTO'su ──
[Mapper]
public partial class TrendyolCategoryToTreeNodeDtoMapper : MapperBase<TrendyolCategory, TrendyolCategoryTreeNodeDto>
{
    public override partial TrendyolCategoryTreeNodeDto Map(TrendyolCategory source);
    public override partial void Map(TrendyolCategory source, TrendyolCategoryTreeNodeDto destination);
}

// ── Trendyol marka cache'i (host-global, write-through) → arama DTO'su ──
//    ExternalId (long) → BrandId (long, ad farkı için MapProperty); Name/IsLuxury ada göre otomatik.
[Mapper]
public partial class TrendyolBrandToDtoMapper : MapperBase<TrendyolBrand, TrendyolBrandDto>
{
    [MapProperty(nameof(TrendyolBrand.ExternalId), nameof(TrendyolBrandDto.BrandId))]
    public override partial TrendyolBrandDto Map(TrendyolBrand source);

    [MapProperty(nameof(TrendyolBrand.ExternalId), nameof(TrendyolBrandDto.BrandId))]
    public override partial void Map(TrendyolBrand source, TrendyolBrandDto destination);
}

// ── Etsy seller taxonomy (host-global taksonomi) → ağaç düğüm DTO'su ──
[Mapper]
public partial class EtsyTaxonomyToTreeNodeDtoMapper : MapperBase<EtsyTaxonomy, EtsyTaxonomyTreeNodeDto>
{
    public override partial EtsyTaxonomyTreeNodeDto Map(EtsyTaxonomy source);
    public override partial void Map(EtsyTaxonomy source, EtsyTaxonomyTreeNodeDto destination);
}

// ── N11 adres (host-global İl/İlçe) → DTO ──
[Mapper]
public partial class N11CityToDtoMapper : MapperBase<N11City, N11CityDto>
{
    public override partial N11CityDto Map(N11City source);
    public override partial void Map(N11City source, N11CityDto destination);
}

[Mapper]
public partial class N11DistrictToDtoMapper : MapperBase<N11District, N11DistrictDto>
{
    public override partial N11DistrictDto Map(N11District source);
    public override partial void Map(N11District source, N11DistrictDto destination);
}

// ── N11 kargo firması (host-global) → DTO ──
[Mapper]
public partial class N11ShipmentCompanyToDtoMapper : MapperBase<N11ShipmentCompany, N11ShipmentCompanyDto>
{
    public override partial N11ShipmentCompanyDto Map(N11ShipmentCompany source);
    public override partial void Map(N11ShipmentCompany source, N11ShipmentCompanyDto destination);
}

// ── N11 kargo şablonu (per-kanal) → DTO. Gömülü Address VO → N11ShipmentAddressDto nested-otomatik;
//    id-only ref listeleri (kargo firması/il) düz kopya. CompanyId/TenantId/audit = source-only. ──
[Mapper]
public partial class N11ShipmentTemplateToDtoMapper : MapperBase<N11ShipmentTemplate, N11ShipmentTemplateDto>
{
    public override partial N11ShipmentTemplateDto Map(N11ShipmentTemplate source);
    public override partial void Map(N11ShipmentTemplate source, N11ShipmentTemplateDto destination);
}

// ── N11 ürün listeleme → DTO. Owned Attributes/SpecialInfo (name/value · key/value) nested-otomatik eşlenir. ──
[Mapper]
public partial class SalesChannelTrN11ProductToDtoMapper : MapperBase<SalesChannelTrN11Product, SalesChannelTrN11ProductDto>
{
    public override partial SalesChannelTrN11ProductDto Map(SalesChannelTrN11Product source);
    public override partial void Map(SalesChannelTrN11Product source, SalesChannelTrN11ProductDto destination);
}

// GetDto → Create/Update (drill persist yolu: DrillList düzenlenen GetDto'yu bu input'lara çevirir). Şartlı kargo
// (read-only) hedefte yok → source-only (RMG020 uyarısı beklenir). Nested N11ShipmentAddressDto aynı tip → kopya.
[Mapper] public partial class N11ShipmentTemplateGetToCreateMapper : MapperBase<N11ShipmentTemplateDto, N11ShipmentTemplateCreateDto>
{
    public override partial N11ShipmentTemplateCreateDto Map(N11ShipmentTemplateDto source);
    public override partial void Map(N11ShipmentTemplateDto source, N11ShipmentTemplateCreateDto destination);
}
[Mapper] public partial class N11ShipmentTemplateGetToUpdateMapper : MapperBase<N11ShipmentTemplateDto, N11ShipmentTemplateUpdateDto>
{
    public override partial N11ShipmentTemplateUpdateDto Map(N11ShipmentTemplateDto source);
    public override partial void Map(N11ShipmentTemplateDto source, N11ShipmentTemplateUpdateDto destination);
}

// N11 ürün listeleme GetDto → Create/Update (drill persist yolu). Nested Attributes/SpecialInfo aynı tip → kopya.
// Durum alanları (N11ProductId/SaleStatus/... ) hedef input'ta yok → source-only (RMG020 uyarısı beklenir).
[Mapper] public partial class SalesChannelTrN11ProductGetToCreateMapper : MapperBase<SalesChannelTrN11ProductDto, SalesChannelTrN11ProductCreateDto>
{
    public override partial SalesChannelTrN11ProductCreateDto Map(SalesChannelTrN11ProductDto source);
    public override partial void Map(SalesChannelTrN11ProductDto source, SalesChannelTrN11ProductCreateDto destination);
}
[Mapper] public partial class SalesChannelTrN11ProductGetToUpdateMapper : MapperBase<SalesChannelTrN11ProductDto, SalesChannelTrN11ProductUpdateDto>
{
    public override partial SalesChannelTrN11ProductUpdateDto Map(SalesChannelTrN11ProductDto source);
    public override partial void Map(SalesChannelTrN11ProductDto source, SalesChannelTrN11ProductUpdateDto destination);
}

// ── Etsy ürün listeleme → DTO. Owned ListingAttributes/SpecialInfo/Skus nested-otomatik eşlenir; tek-alanlı owned
//    Tag/Material → düz string listesi (user-defined element mapping MapTag/MapMaterial'ı Mapperly otomatik kullanır). ──
[Mapper]
public partial class SalesChannelEtsyProductToDtoMapper : MapperBase<SalesChannelEtsyProduct, SalesChannelEtsyProductDto>
{
    // Taksonomi görüntü alanları entity'de YOK → okuma anında AppService zenginleştirir (synced taxonomy tablosundan çözülür).
    [MapperIgnoreTarget(nameof(SalesChannelEtsyProductDto.TaxonomyName))]
    [MapperIgnoreTarget(nameof(SalesChannelEtsyProductDto.TaxonomyIsStale))]
    public override partial SalesChannelEtsyProductDto Map(SalesChannelEtsyProduct source);

    [MapperIgnoreTarget(nameof(SalesChannelEtsyProductDto.TaxonomyName))]
    [MapperIgnoreTarget(nameof(SalesChannelEtsyProductDto.TaxonomyIsStale))]
    public override partial void Map(SalesChannelEtsyProduct source, SalesChannelEtsyProductDto destination);

    private static string MapTag(SalesChannelEtsyProductTag tag)
    {
        return tag.Value;
    }

    private static string MapMaterial(SalesChannelEtsyProductMaterial material)
    {
        return material.Value;
    }
}

// Etsy ürün listeleme GetDto → Create/Update (ürün grafı persist yolu). Nested owned/graf aynı tip → kopya; Tags/Materials
// List<string> düz kopya. Durum alanları (EtsyListingId/ListingState/...) hedef input'ta yok → source-only (RMG020 uyarısı beklenir).
[Mapper] public partial class SalesChannelEtsyProductGetToCreateMapper : MapperBase<SalesChannelEtsyProductDto, SalesChannelEtsyProductCreateDto>
{
    public override partial SalesChannelEtsyProductCreateDto Map(SalesChannelEtsyProductDto source);
    public override partial void Map(SalesChannelEtsyProductDto source, SalesChannelEtsyProductCreateDto destination);
}
[Mapper] public partial class SalesChannelEtsyProductGetToUpdateMapper : MapperBase<SalesChannelEtsyProductDto, SalesChannelEtsyProductUpdateDto>
{
    public override partial SalesChannelEtsyProductUpdateDto Map(SalesChannelEtsyProductDto source);
    public override partial void Map(SalesChannelEtsyProductDto source, SalesChannelEtsyProductUpdateDto destination);
}

// ── Trendyol ürün listeleme → DTO + GetDto→Create/Update (drill persist yolu). Owned Attributes (id-bazlı) nested. ──
[Mapper]
public partial class SalesChannelTrTrendyolProductToDtoMapper : MapperBase<SalesChannelTrTrendyolProduct, SalesChannelTrTrendyolProductDto>
{
    public override partial SalesChannelTrTrendyolProductDto Map(SalesChannelTrTrendyolProduct source);
    public override partial void Map(SalesChannelTrTrendyolProduct source, SalesChannelTrTrendyolProductDto destination);
}
[Mapper] public partial class SalesChannelTrTrendyolProductGetToCreateMapper : MapperBase<SalesChannelTrTrendyolProductDto, SalesChannelTrTrendyolProductCreateDto>
{
    public override partial SalesChannelTrTrendyolProductCreateDto Map(SalesChannelTrTrendyolProductDto source);
    public override partial void Map(SalesChannelTrTrendyolProductDto source, SalesChannelTrTrendyolProductCreateDto destination);
}
[Mapper] public partial class SalesChannelTrTrendyolProductGetToUpdateMapper : MapperBase<SalesChannelTrTrendyolProductDto, SalesChannelTrTrendyolProductUpdateDto>
{
    public override partial SalesChannelTrTrendyolProductUpdateDto Map(SalesChannelTrTrendyolProductDto source);
    public override partial void Map(SalesChannelTrTrendyolProductDto source, SalesChannelTrTrendyolProductUpdateDto destination);
}

// ── Scrap (statik mapper → Mapperly; IsGlobal + FollowingUnitCode AppService'te/ApplyUnitCodes ile set) ──

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class ScrapToGetDtoMapper : MapperBase<Scrap, ScrapGetDto>
{
    [MapperIgnoreTarget(nameof(ScrapGetDto.IsGlobal))]
    [MapperIgnoreTarget(nameof(ScrapGetDto.FollowingUnitCode))]
    public override partial ScrapGetDto Map(Scrap source);
    public override partial void Map(Scrap source, ScrapGetDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class ScrapToListDtoMapper : MapperBase<Scrap, ScrapListDto>
{
    [MapperIgnoreTarget(nameof(ScrapListDto.IsGlobal))]
    [MapperIgnoreTarget(nameof(ScrapListDto.FollowingUnitCode))]
    public override partial ScrapListDto Map(Scrap source);
    public override partial void Map(Scrap source, ScrapListDto destination);
}

// ── Metal (Scrap deseni: IsGlobal + FollowingUnitCode ignore) ──

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class MetalToGetDtoMapper : MapperBase<Metal, MetalGetDto>
{
    [MapperIgnoreTarget(nameof(MetalGetDto.IsGlobal))]
    [MapperIgnoreTarget(nameof(MetalGetDto.FollowingUnitCode))]
    [MapperIgnoreTarget(nameof(MetalGetDto.Documents))]     // agnostik Document — AppService yükler
    [MapperIgnoreTarget(nameof(MetalGetDto.Notes))]         // agnostik Note — AppService yükler
    [MapperIgnoreTarget(nameof(MetalGetDto.Attributes))]    // varyant grafı — AppService yükler
    [MapperIgnoreTarget(nameof(MetalGetDto.Variants))]      // varyant grafı — AppService yükler
    public override partial MetalGetDto Map(Metal source);
    public override partial void Map(Metal source, MetalGetDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class MetalToListDtoMapper : MapperBase<Metal, MetalListDto>
{
    [MapperIgnoreTarget(nameof(MetalListDto.IsGlobal))]
    [MapperIgnoreTarget(nameof(MetalListDto.FollowingUnitCode))]
    [MapperIgnoreTarget(nameof(MetalListDto.ImagePreviewUrl))]   // EnrichListAsync doldurur (thumbnail/URL)
    public override partial MetalListDto Map(Metal source);
    public override partial void Map(Metal source, MetalListDto destination);
}

// ── Future (Metal deseni: IsGlobal + FollowingUnitCode ignore) ──

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class FutureToGetDtoMapper : MapperBase<Future, FutureGetDto>
{
    [MapperIgnoreTarget(nameof(FutureGetDto.IsGlobal))]
    [MapperIgnoreTarget(nameof(FutureGetDto.FollowingUnitCode))]
    public override partial FutureGetDto Map(Future source);
    public override partial void Map(Future source, FutureGetDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class FutureToListDtoMapper : MapperBase<Future, FutureListDto>
{
    [MapperIgnoreTarget(nameof(FutureListDto.IsGlobal))]
    [MapperIgnoreTarget(nameof(FutureListDto.FollowingUnitCode))]
    public override partial FutureListDto Map(Future source);
    public override partial void Map(Future source, FutureListDto destination);
}

// ── Jewelry / Stone (Service deseni: yalnız IsGlobal ignore; GroupCode entity'de) ──

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class JewelryToGetDtoMapper : MapperBase<Jewelry, JewelryGetDto>
{
    [MapperIgnoreTarget(nameof(JewelryGetDto.IsGlobal))]
    [MapperIgnoreTarget(nameof(JewelryGetDto.Documents))]    // agnostik Document — AppService yükler
    [MapperIgnoreTarget(nameof(JewelryGetDto.Notes))]        // agnostik Note — AppService yükler
    [MapperIgnoreTarget(nameof(JewelryGetDto.Attributes))]   // varyant grafı — AppService yükler
    [MapperIgnoreTarget(nameof(JewelryGetDto.Variants))]     // varyant grafı — AppService yükler
    public override partial JewelryGetDto Map(Jewelry source);
    public override partial void Map(Jewelry source, JewelryGetDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class JewelryToListDtoMapper : MapperBase<Jewelry, JewelryListDto>
{
    [MapperIgnoreTarget(nameof(JewelryListDto.IsGlobal))]
    [MapperIgnoreTarget(nameof(JewelryListDto.ImagePreviewUrl))]   // EnrichListAsync doldurur (ana varyant poster'ı)
    public override partial JewelryListDto Map(Jewelry source);
    public override partial void Map(Jewelry source, JewelryListDto destination);
}

// ── Good / Mamül (Jewelry deseni: yalnız IsGlobal ignore) ──

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class GoodToGetDtoMapper : MapperBase<Good, GoodGetDto>
{
    [MapperIgnoreTarget(nameof(GoodGetDto.IsGlobal))]
    [MapperIgnoreTarget(nameof(GoodGetDto.Suppliers))]      // ayrı tablo — AppService yükler
    [MapperIgnoreTarget(nameof(GoodGetDto.Documents))]      // agnostik Document — AppService yükler
    [MapperIgnoreTarget(nameof(GoodGetDto.Notes))]          // agnostik Note — AppService yükler
    [MapperIgnoreTarget(nameof(GoodGetDto.Attributes))]     // varyant grafı — AppService yükler
    [MapperIgnoreTarget(nameof(GoodGetDto.Variants))]       // varyant grafı — AppService yükler
    public override partial GoodGetDto Map(Good source);
    public override partial void Map(Good source, GoodGetDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class GoodToListDtoMapper : MapperBase<Good, GoodListDto>
{
    // Fiyat (alış/satış + birim) artık Good'da DEĞİL → ANA VARYANT'tan enrich edilir (GoodAppService.EnrichListPricingAsync).
    [MapperIgnoreTarget(nameof(GoodListDto.IsGlobal))]
    [MapperIgnoreTarget(nameof(GoodListDto.EntryPrice))]
    [MapperIgnoreTarget(nameof(GoodListDto.EntryPriceUnitId))]
    [MapperIgnoreTarget(nameof(GoodListDto.ExitPrice))]
    [MapperIgnoreTarget(nameof(GoodListDto.ExitPriceUnitId))]
    [MapperIgnoreTarget(nameof(GoodListDto.ImagePreviewUrl))]   // EnrichPreviewsAsync doldurur (ana varyant poster'ı)
    public override partial GoodListDto Map(Good source);
    public override partial void Map(Good source, GoodListDto destination);
}

// ── SpecialCode / Özel Kod (Good deseni: yalnız IsGlobal ignore) ──

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class SpecialCodeToGetDtoMapper : MapperBase<SpecialCode, SpecialCodeGetDto>
{
    [MapperIgnoreTarget(nameof(SpecialCodeGetDto.IsGlobal))]
    public override partial SpecialCodeGetDto Map(SpecialCode source);
    public override partial void Map(SpecialCode source, SpecialCodeGetDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class SpecialCodeToListDtoMapper : MapperBase<SpecialCode, SpecialCodeListDto>
{
    [MapperIgnoreTarget(nameof(SpecialCodeListDto.IsGlobal))]
    public override partial SpecialCodeListDto Map(SpecialCode source);
    public override partial void Map(SpecialCode source, SpecialCodeListDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class StoneToGetDtoMapper : MapperBase<Stone, StoneGetDto>
{
    [MapperIgnoreTarget(nameof(StoneGetDto.IsGlobal))]
    [MapperIgnoreTarget(nameof(StoneGetDto.Documents))]     // agnostik Document — AppService yükler
    [MapperIgnoreTarget(nameof(StoneGetDto.Notes))]         // agnostik Note — AppService yükler
    [MapperIgnoreTarget(nameof(StoneGetDto.Attributes))]    // varyant grafı — AppService yükler
    [MapperIgnoreTarget(nameof(StoneGetDto.Variants))]      // varyant grafı — AppService yükler
    public override partial StoneGetDto Map(Stone source);
    public override partial void Map(Stone source, StoneGetDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class StoneToListDtoMapper : MapperBase<Stone, StoneListDto>
{
    [MapperIgnoreTarget(nameof(StoneListDto.IsGlobal))]
    [MapperIgnoreTarget(nameof(StoneListDto.ImagePreviewUrl))]   // EnrichListAsync doldurur (ana varyant poster'ı)
    public override partial StoneListDto Map(Stone source);
    public override partial void Map(Stone source, StoneListDto destination);
}

// ── UserScopedGrant (statik ToDto → Mapperly; düz 1:1, enrichment yok) ──

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class UserScopedGrantToDtoMapper : MapperBase<UserScopedGrant, UserScopedGrantDto>
{
    public override partial UserScopedGrantDto Map(UserScopedGrant source);
    public override partial void Map(UserScopedGrant source, UserScopedGrantDto destination);
}

[Mapper] public partial class ServiceGetToCreateMapper : MapperBase<ServiceGetDto, ServiceCreateDto>
{
    public override partial ServiceCreateDto Map(ServiceGetDto source);
    public override partial void Map(ServiceGetDto source, ServiceCreateDto destination);
}
[Mapper] public partial class ServiceGetToUpdateMapper : MapperBase<ServiceGetDto, ServiceUpdateDto>
{
    public override partial ServiceUpdateDto Map(ServiceGetDto source);
    public override partial void Map(ServiceGetDto source, ServiceUpdateDto destination);
}

[Mapper] public partial class SalesChannelTrN11GetToCreateMapper : MapperBase<SalesChannelTrN11GetDto, SalesChannelTrN11CreateDto>
{
    public override partial SalesChannelTrN11CreateDto Map(SalesChannelTrN11GetDto source);
    public override partial void Map(SalesChannelTrN11GetDto source, SalesChannelTrN11CreateDto destination);
}
[Mapper] public partial class SalesChannelTrN11GetToUpdateMapper : MapperBase<SalesChannelTrN11GetDto, SalesChannelTrN11UpdateDto>
{
    public override partial SalesChannelTrN11UpdateDto Map(SalesChannelTrN11GetDto source);
    public override partial void Map(SalesChannelTrN11GetDto source, SalesChannelTrN11UpdateDto destination);
}
[Mapper] public partial class SalesChannelTrTrendyolGetToCreateMapper : MapperBase<SalesChannelTrTrendyolGetDto, SalesChannelTrTrendyolCreateDto>
{
    public override partial SalesChannelTrTrendyolCreateDto Map(SalesChannelTrTrendyolGetDto source);
    public override partial void Map(SalesChannelTrTrendyolGetDto source, SalesChannelTrTrendyolCreateDto destination);
}
[Mapper] public partial class SalesChannelTrTrendyolGetToUpdateMapper : MapperBase<SalesChannelTrTrendyolGetDto, SalesChannelTrTrendyolUpdateDto>
{
    public override partial SalesChannelTrTrendyolUpdateDto Map(SalesChannelTrTrendyolGetDto source);
    public override partial void Map(SalesChannelTrTrendyolGetDto source, SalesChannelTrTrendyolUpdateDto destination);
}
// Etsy GetDto → Create/Update (edit host persist yolu). Salt-okunur durum/görüntü alanları (IsConnected/ShopId/ShopName)
// hedef input'ta yok → source-only (RMG020 uyarısı beklenir).
[Mapper] public partial class SalesChannelEtsyGetToCreateMapper : MapperBase<SalesChannelEtsyGetDto, SalesChannelEtsyCreateDto>
{
    public override partial SalesChannelEtsyCreateDto Map(SalesChannelEtsyGetDto source);
    public override partial void Map(SalesChannelEtsyGetDto source, SalesChannelEtsyCreateDto destination);
}
[Mapper] public partial class SalesChannelEtsyGetToUpdateMapper : MapperBase<SalesChannelEtsyGetDto, SalesChannelEtsyUpdateDto>
{
    public override partial SalesChannelEtsyUpdateDto Map(SalesChannelEtsyGetDto source);
    public override partial void Map(SalesChannelEtsyGetDto source, SalesChannelEtsyUpdateDto destination);
}

[Mapper] public partial class FutureGetToCreateMapper : MapperBase<FutureGetDto, FutureCreateDto>
{
    public override partial FutureCreateDto Map(FutureGetDto source);
    public override partial void Map(FutureGetDto source, FutureCreateDto destination);
}
[Mapper] public partial class FutureGetToUpdateMapper : MapperBase<FutureGetDto, FutureUpdateDto>
{
    public override partial FutureUpdateDto Map(FutureGetDto source);
    public override partial void Map(FutureGetDto source, FutureUpdateDto destination);
}

[Mapper] public partial class ScrapGetToCreateMapper : MapperBase<ScrapGetDto, ScrapCreateDto>
{
    public override partial ScrapCreateDto Map(ScrapGetDto source);
    public override partial void Map(ScrapGetDto source, ScrapCreateDto destination);
}
[Mapper] public partial class ScrapGetToUpdateMapper : MapperBase<ScrapGetDto, ScrapUpdateDto>
{
    public override partial ScrapUpdateDto Map(ScrapGetDto source);
    public override partial void Map(ScrapGetDto source, ScrapUpdateDto destination);
}

[Mapper] public partial class MetalGetToCreateMapper : MapperBase<MetalGetDto, MetalCreateDto>
{
    public override partial MetalCreateDto Map(MetalGetDto source);
    public override partial void Map(MetalGetDto source, MetalCreateDto destination);
}
[Mapper] public partial class MetalGetToUpdateMapper : MapperBase<MetalGetDto, MetalUpdateDto>
{
    public override partial MetalUpdateDto Map(MetalGetDto source);
    public override partial void Map(MetalGetDto source, MetalUpdateDto destination);
}

[Mapper] public partial class StoneGetToCreateMapper : MapperBase<StoneGetDto, StoneCreateDto>
{
    public override partial StoneCreateDto Map(StoneGetDto source);
    public override partial void Map(StoneGetDto source, StoneCreateDto destination);
}
[Mapper] public partial class StoneGetToUpdateMapper : MapperBase<StoneGetDto, StoneUpdateDto>
{
    public override partial StoneUpdateDto Map(StoneGetDto source);
    public override partial void Map(StoneGetDto source, StoneUpdateDto destination);
}

[Mapper] public partial class JewelryGetToCreateMapper : MapperBase<JewelryGetDto, JewelryCreateDto>
{
    public override partial JewelryCreateDto Map(JewelryGetDto source);
    public override partial void Map(JewelryGetDto source, JewelryCreateDto destination);
}
[Mapper] public partial class JewelryGetToUpdateMapper : MapperBase<JewelryGetDto, JewelryUpdateDto>
{
    public override partial JewelryUpdateDto Map(JewelryGetDto source);
    public override partial void Map(JewelryGetDto source, JewelryUpdateDto destination);
}

[Mapper] public partial class GoodGetToCreateMapper : MapperBase<GoodGetDto, GoodCreateDto>
{
    public override partial GoodCreateDto Map(GoodGetDto source);
    public override partial void Map(GoodGetDto source, GoodCreateDto destination);
}
[Mapper] public partial class GoodGetToUpdateMapper : MapperBase<GoodGetDto, GoodUpdateDto>
{
    public override partial GoodUpdateDto Map(GoodGetDto source);
    public override partial void Map(GoodGetDto source, GoodUpdateDto destination);
}

[Mapper] public partial class SpecialCodeGetToCreateMapper : MapperBase<SpecialCodeGetDto, SpecialCodeCreateDto>
{
    public override partial SpecialCodeCreateDto Map(SpecialCodeGetDto source);
    public override partial void Map(SpecialCodeGetDto source, SpecialCodeCreateDto destination);
}
[Mapper] public partial class SpecialCodeGetToUpdateMapper : MapperBase<SpecialCodeGetDto, SpecialCodeUpdateDto>
{
    public override partial SpecialCodeUpdateDto Map(SpecialCodeGetDto source);
    public override partial void Map(SpecialCodeGetDto source, SpecialCodeUpdateDto destination);
}

[Mapper] public partial class AccountGetToCreateMapper : MapperBase<AccountGetDto, AccountCreateDto>
{
    public override partial AccountCreateDto Map(AccountGetDto source);
    public override partial void Map(AccountGetDto source, AccountCreateDto destination);
}
[Mapper] public partial class AccountGetToUpdateMapper : MapperBase<AccountGetDto, AccountUpdateDto>
{
    public override partial AccountUpdateDto Map(AccountGetDto source);
    public override partial void Map(AccountGetDto source, AccountUpdateDto destination);
}

// SubAccount edit'i PersistentCoordinator üzerinden koşulsuz Map<GetDto,Create/UpdateDto> çağırır —
// bu mapper'lar yokken kaydet/güncelle runtime'da "No object mapping was found" fırlatıyordu (entegrasyon analizi E-3).
// Product: PersistentCoordinator koşulsuz Map<GetDto,Create/UpdateDto> çağırır (Account grafı deseni).
// Varyant grafı (List<ProductVariantGraphDto>) aynı tip → element-kopya; entity→GetDto elle projekte edilir (AppService).
[Mapper] public partial class ProductGetToCreateMapper : MapperBase<ProductGetDto, ProductCreateDto>
{
    public override partial ProductCreateDto Map(ProductGetDto source);
    public override partial void Map(ProductGetDto source, ProductCreateDto destination);
}
[Mapper] public partial class ProductGetToUpdateMapper : MapperBase<ProductGetDto, ProductUpdateDto>
{
    public override partial ProductUpdateDto Map(ProductGetDto source);
    public override partial void Map(ProductGetDto source, ProductUpdateDto destination);
}

// ── SubstitutionGroup (Muadil grubu — Account grafı deseni) ──
// Items grafı entity'de karşılıksız (ayrı aggregate satırlar) → GetDto'da AppService doldurur (ignore).
// GetDto→Create/Update: PersistentCoordinator koşulsuz Map çağırır; Items aynı tip → element-kopya.

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class SubstitutionGroupToGetDtoMapper : MapperBase<SubstitutionGroup, SubstitutionGroupGetDto>
{
    [MapperIgnoreTarget(nameof(SubstitutionGroupGetDto.Items))]
    public override partial SubstitutionGroupGetDto Map(SubstitutionGroup source);
    public override partial void Map(SubstitutionGroup source, SubstitutionGroupGetDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class SubstitutionGroupToListDtoMapper : MapperBase<SubstitutionGroup, SubstitutionGroupListDto>
{
    public override partial SubstitutionGroupListDto Map(SubstitutionGroup source);
    public override partial void Map(SubstitutionGroup source, SubstitutionGroupListDto destination);
}

[Mapper] public partial class SubstitutionGroupGetToCreateMapper : MapperBase<SubstitutionGroupGetDto, SubstitutionGroupCreateDto>
{
    public override partial SubstitutionGroupCreateDto Map(SubstitutionGroupGetDto source);
    public override partial void Map(SubstitutionGroupGetDto source, SubstitutionGroupCreateDto destination);
}
[Mapper] public partial class SubstitutionGroupGetToUpdateMapper : MapperBase<SubstitutionGroupGetDto, SubstitutionGroupUpdateDto>
{
    public override partial SubstitutionGroupUpdateDto Map(SubstitutionGroupGetDto source);
    public override partial void Map(SubstitutionGroupGetDto source, SubstitutionGroupUpdateDto destination);
}

// ── Order (NÖTR sipariş — company-owned). SalesChannelCode enrich (id-only referanstan) + Lines (ayrı repo)
//    AppService'te doldurulur → mapper'da ignore. CompanyId/TenantId/audit = source-only. ──

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class OrderToListDtoMapper : MapperBase<Order, OrderListDto>
{
    [MapperIgnoreTarget(nameof(OrderListDto.SalesChannelCode))]
    [MapperIgnoreTarget(nameof(OrderListDto.Items))]   // AppService enrich (ayrı repo + zengin kalem detayı)
    public override partial OrderListDto Map(Order source);
    public override partial void Map(Order source, OrderListDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class OrderToDtoMapper : MapperBase<Order, OrderDto>
{
    [MapperIgnoreTarget(nameof(OrderDto.SalesChannelCode))]
    [MapperIgnoreTarget(nameof(OrderDto.Lines))]
    [MapperIgnoreTarget(nameof(OrderDto.CargoProvider))]        // AppService enrich eder (düzeltme ?? orijinal)
    [MapperIgnoreTarget(nameof(OrderDto.CargoTrackingNumber))]  // AppService enrich eder (düzeltme ?? orijinal)
    [MapperIgnoreTarget(nameof(OrderDto.Buyer))]                // AppService enrich eder (Order'da yok — Detail'den)
    [MapperIgnoreTarget(nameof(OrderDto.BillingAddress))]       // AppService enrich eder (Order'da yok — Detail'den)
    [MapperIgnoreTarget(nameof(OrderDto.ShippingAddress))]      // AppService enrich eder (Order'da yok — Detail'den)
    [MapperIgnoreTarget(nameof(OrderDto.ActionInputNumberOfPackages))]   // UI-only (property default'u geçerli)
    [MapperIgnoreTarget(nameof(OrderDto.PendingLineCount))]              // AppService ayrı sorguyla hesaplar
    [MapperIgnoreTarget(nameof(OrderDto.CountryId))]                     // AppService çözer (host coğrafyadan TR id'si)
    public override partial OrderDto Map(Order source);
    public override partial void Map(Order source, OrderDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class OrderLineToDtoMapper : MapperBase<OrderLine, OrderLineDto>
{
    public override partial OrderLineDto Map(OrderLine source);
    public override partial void Map(OrderLine source, OrderLineDto destination);
}

// MELEZ sipariş-kalemi satırı: line alanları buradan (OrderLine.Id → OrderLineId); order başlığı (kanal/no/müşteri/
// tarih/tutar/kargo) AppService'te enrich edilir → ignore.
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class OrderLineToItemListDtoMapper : MapperBase<OrderLine, OrderItemListDto>
{
    [MapProperty(nameof(OrderLine.Id), nameof(OrderItemListDto.Id))]
    [MapperIgnoreTarget(nameof(OrderItemListDto.SalesChannelId))]
    [MapperIgnoreTarget(nameof(OrderItemListDto.ChannelType))]
    [MapperIgnoreTarget(nameof(OrderItemListDto.SalesChannelCode))]
    [MapperIgnoreTarget(nameof(OrderItemListDto.OrderNumber))]
    [MapperIgnoreTarget(nameof(OrderItemListDto.OrderDate))]
    [MapperIgnoreTarget(nameof(OrderItemListDto.NeutralStatus))]
    [MapperIgnoreTarget(nameof(OrderItemListDto.RemoteStatus))]
    [MapperIgnoreTarget(nameof(OrderItemListDto.CustomerName))]
    [MapperIgnoreTarget(nameof(OrderItemListDto.OrderTotalAmount))]
    [MapperIgnoreTarget(nameof(OrderItemListDto.CargoProvider))]
    [MapperIgnoreTarget(nameof(OrderItemListDto.CargoTrackingNumber))]
    [MapperIgnoreTarget(nameof(OrderItemListDto.ItemDetail))]   // AppService enrich (snapshot'tan RemoteLineId ile)
    public override partial OrderItemListDto Map(OrderLine source);
    public override partial void Map(OrderLine source, OrderItemListDto destination);
}

[Mapper] public partial class SubAccountGetToCreateMapper : MapperBase<SubAccountGetDto, SubAccountCreateDto>
{
    public override partial SubAccountCreateDto Map(SubAccountGetDto source);
    public override partial void Map(SubAccountGetDto source, SubAccountCreateDto destination);
}
[Mapper] public partial class SubAccountGetToUpdateMapper : MapperBase<SubAccountGetDto, SubAccountUpdateDto>
{
    public override partial SubAccountUpdateDto Map(SubAccountGetDto source);
    public override partial void Map(SubAccountGetDto source, SubAccountUpdateDto destination);
}

// ── Confirmation / Teyit (organizasyon-içi ayna onayı; entity→DTO). Vault/CurrencyUnit kodları (id-only
//    referanstan) AppService'te çözülür → mapper'da ignore. Payload'lar (her tarafın KENDİ satırı; opak)
//    listede TAŞINMAZ → DTO'da YOK; ayrıca GetPayloadAsync ile istenir. TenantId/CompanyId source-only. ──
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class ConfirmationToDtoMapper : MapperBase<Confirmation, ConfirmationDto>
{
    [MapperIgnoreTarget(nameof(ConfirmationDto.InitiatorVaultCode))]
    [MapperIgnoreTarget(nameof(ConfirmationDto.CounterpartyVaultCode))]
    [MapperIgnoreTarget(nameof(ConfirmationDto.MainUnitCode))]
    [MapperIgnoreTarget(nameof(ConfirmationDto.PayUnitCode))]
    // UI-gating bayrakları entity'de YOK → AppService.GetListAsync elle set eder (IScopedGrantResolver sonucu).
    [MapperIgnoreTarget(nameof(ConfirmationDto.IsInitiatorMine))]
    [MapperIgnoreTarget(nameof(ConfirmationDto.IsCounterpartyMine))]
    public override partial ConfirmationDto Map(Confirmation source);
    public override partial void Map(Confirmation source, ConfirmationDto destination);
}

// ── Media (DAM; entity→DTO). PosterUrl/ContentUrl/HasPoster AppService'te hesaplanır (Id-scoped stream endpoint +
//    LastModificationTime cache-buster) → mapper'da ignore. ──
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class MediaToDtoMapper : MapperBase<Media, MediaDto>
{
    [MapperIgnoreTarget(nameof(MediaDto.HasPoster))]
    [MapperIgnoreTarget(nameof(MediaDto.PosterUrl))]
    [MapperIgnoreTarget(nameof(MediaDto.ContentUrl))]
    public override partial MediaDto Map(Media source);
    public override partial void Map(Media source, MediaDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class MediaFolderToDtoMapper : MapperBase<MediaFolder, MediaFolderDto>
{
    public override partial MediaFolderDto Map(MediaFolder source);
    public override partial void Map(MediaFolder source, MediaFolderDto destination);
}

[Mapper] public partial class EntityVariantGraphDtoToMetalVariantGraphDtoMapper : MapperBase<Integration.TradeXpress.Variants.EntityVariantGraphDto, Integration.TradeXpress.Metals.MetalVariantGraphDto>
{
    public override partial Integration.TradeXpress.Metals.MetalVariantGraphDto Map(Integration.TradeXpress.Variants.EntityVariantGraphDto source);
    public override partial void Map(Integration.TradeXpress.Variants.EntityVariantGraphDto source, Integration.TradeXpress.Metals.MetalVariantGraphDto destination);
}

// ── Geography (okuma DTO'ları — on-demand import sonrası DB'den; GeographyAppService) ──

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class AdministrativeAreaToDtoMapper : MapperBase<AdministrativeArea, AdministrativeAreaDto>
{
    public override partial AdministrativeAreaDto Map(AdministrativeArea source);
    public override partial void Map(AdministrativeArea source, AdministrativeAreaDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class LocalityToDtoMapper : MapperBase<Locality, LocalityDto>
{
    public override partial LocalityDto Map(Locality source);
    public override partial void Map(Locality source, LocalityDto destination);
}
