using System;
using Integration.Framework.Base.Dtos.Interfaces;
using Volo.Abp.Application.Dtos;

namespace Integration.Framework.Base.Dtos;

/// <summary>
/// Katalog (host + tenant scoped basit tanım) List DTO'larının ortak tabanı.
/// Code/Name list tarafında attribute taşımaz → düz property yeterli.
/// <see cref="IHasCode"/> BİLEREK burada yok — Get tarafında türev karar verir
/// (ör. Country başlıkta kod göstermez).
/// </summary>
public abstract class CatalogListDtoBase : EntityDto<Guid>, IListDto<Guid>, IIsActive, IHostScoped
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsGlobal { get; set; }
}

/// <summary>
/// Katalog Get (edit formu) DTO'larının ortak tabanı. Code/Name <c>virtual</c>:
/// max-length'ler entity-özel (*Consts) olduğundan validation attribute'ları
/// TÜREVDEKİ override üzerinde yaşar — base attribute taşımaz, validation kaybolmaz
/// (DataAnnotations en türev property'nin attribute'larını okur).
/// </summary>
public abstract class CatalogGetDtoBase : EntityDto<Guid>, IGetDto<Guid>, IHostScoped
{
    public virtual string Code { get; set; } = string.Empty;
    public virtual string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsGlobal { get; set; }
}

/// <summary>Katalog Create DTO ortak tabanı — Code/Name attribute'ları türevde (bkz. <see cref="CatalogGetDtoBase"/>).</summary>
public abstract class CatalogCreateDtoBase : ICreateDto
{
    public virtual string Code { get; set; } = string.Empty;
    public virtual string Name { get; set; } = string.Empty;
}

/// <summary>Katalog Update DTO ortak tabanı — Code/Name attribute'ları türevde (bkz. <see cref="CatalogGetDtoBase"/>).
/// Code DÜZENLENEBİLİR (ürün kuralı 2026-07-04: host CurrencyUnit kayıtları dışında tüm kodlar değiştirilebilir).</summary>
public abstract class CatalogUpdateDtoBase : IUpdateDto, IHasIsActive
{
    public virtual string Code { get; set; } = string.Empty;
    public virtual string Name { get; set; } = string.Empty;

    /// <summary><see cref="IHasIsActive"/> ile açıkça ilan edilir (2026-08-05): alan zaten vardı, ama arayüz
    /// olmadan bir taban sınıf "aktif→pasif geçişi oluyor mu" sorusunu TİPLİ olarak soramıyordu — yalnız
    /// yansıma ya da tekrarlanan cast'lerle. Şekil aynı, sözleşme artık görünür.</summary>
    public bool IsActive { get; set; }
}
