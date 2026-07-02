using System;

namespace Integration.TradeXpress.Conventions;

/// <summary>
/// Bir navigation property'yi <b>id-only konvansiyonuna BİLİNÇLİ İSTİSNA</b> olarak işaretler. Bu attribute
/// OLMADAN, NavigationConventionTests şunları KIRMIZI yapar:
/// <list type="bullet">
///   <item><c>XId</c> foreign-key + aynı adlı <c>X</c> navigation çifti (aggregate'ler arası id-only olmalı).</item>
///   <item>Karşılığında <c>XId</c> olmayan orphan navigation.</item>
/// </list>
/// MEŞRU kullanım: aggregate-İÇİ child→root navigation (örn. <c>VoucherLine.Voucher</c>) — gerçek nesne
/// grafiği aynı aggregate sınırı içindeyse. Aggregate'ler ARASI (başka root'a) nav için KULLANMA → id tut.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class AllowNavigationAttribute : Attribute
{
    public AllowNavigationAttribute(string reason)
    {
        Reason = reason;
    }

    /// <summary>İstisnanın gerekçesi (neden bu nav meşru) — dokümantasyon + review için zorunlu.</summary>
    public string Reason { get; }
}
