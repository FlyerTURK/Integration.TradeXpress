// Domain projesinin MERKEZİ using'leri. Dosya başlarını "using çöplüğünden" arındırmak için
// paylaşılan namespace'ler burada bir kez (global using) bildirilir; tipler dosyalarda kısa adıyla yazılır.
// Yalnız MODÜL kompozisyonuna özel tek-kullanımlık wiring using'leri (OpenIddict/BlobStoring/Identity/
// AuditLogging vb.) ilgili dosyada (TradeXpressDomainModule) yerel kalır.

// ── BCL ──
global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Linq.Expressions;
global using System.Text.Json.Serialization;
global using System.Threading.Tasks;

// ── Framework (Integration.Framework: FrameworkErrorCodes, StringFieldGuard, tipli exception'lar, normalize extension'ları) ──
global using Integration.Framework;
global using Integration.Framework.Timing;

// ── ABP çekirdek / domain ──
global using Volo.Abp;
global using Volo.Abp.Caching;
global using Volo.Abp.Data;
global using Volo.Abp.DependencyInjection;
global using Volo.Abp.Domain.Entities.Auditing;
global using Volo.Abp.Domain.Repositories;
global using Volo.Abp.Domain.Services;
global using Volo.Abp.Domain.Values;
global using Volo.Abp.Guids;
global using Volo.Abp.MultiTenancy;
global using Volo.Abp.Timing;
global using Volo.Abp.Uow;

// ── TradeXpress alan namespace'leri ──
global using Integration.TradeXpress.MultiTenancy;
global using Integration.TradeXpress.Companies;
global using Integration.TradeXpress.Branches;
global using Integration.TradeXpress.Vaults;
global using Integration.TradeXpress.Countries;
global using Integration.TradeXpress.Cashes;
global using Integration.TradeXpress.Accounts;
global using Integration.TradeXpress.MultiCompany;
global using Integration.TradeXpress.Organization;
global using Integration.TradeXpress.Financials.CurrencyUnits;
global using Integration.TradeXpress.Financials.Parities;
global using Integration.TradeXpress.Financials.ExchangeRates;
global using Integration.TradeXpress.N11Shipments;
