using System.Collections.Generic;
using System.Linq;
using Integration.TradeXpress.Variants;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Components.Shared;

/// <summary>
/// Ana varyantın kimlik alanlarını ÇEKİRDEK emtia formundan yönetir (kod-arkası).
///
/// <para><b>Ana varyant her turda YENİDEN bulunur</b> (önbelleğe alınmaz): kullanıcı varyant gridinden
/// başka bir satırı ana yapabilir ve o an bu pencerenin yeni sahibi göstermesi gerekir. Bir kez bulup
/// saklasaydık form eski varyantı düzenlemeye devam ederdi — kullanıcı doğru alanı düzenlediğini sanarak
/// yanlış satırı değiştirirdi, üstelik hiçbir hata görmeden.</para>
///
/// <para><b>Ana varyant yoksa grup HİÇ çizilmez:</b> yeni (henüz kaydedilmemiş) kayıtta varyant grafı boş
/// olabilir. Boş bir kimlik grubu göstermek, doldurulabilir sanılan ama hiçbir yere yazmayan alanlar
/// demek olurdu.</para>
/// </summary>
public partial class MainVariantIdentityFields<TVariant>
    where TVariant : EntityVariantGraphDto
{
    /// <summary>Sahip kaydın varyant grafı — <b>aynı nesne listesi</b> varyant panelinde de kullanılır.
    /// Kopya DEĞİL: buradaki düzenleme oradaki gridde de görünür ve tek Kaydet ile birlikte persist olur.</summary>
    [Parameter]
    public IEnumerable<TVariant>? Variants { get; set; }

    /// <summary>Silinmemiş ana varyant. Silinmişi ELEMEK şart — kullanıcı ana varyantı silmek üzere
    /// işaretlemişse form onun alanlarını düzenlemeye devam etmemeli.</summary>
    private TVariant? Main
    {
        get
        {
            return Variants?.FirstOrDefault(v => v.IsMain && !v.IsDeleted);
        }
    }
}
