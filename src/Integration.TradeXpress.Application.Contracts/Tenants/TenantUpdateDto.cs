using System.Collections.Generic;
using Integration.Framework.Base.Dtos.Interfaces;
using Integration.TradeXpress.Companies;
using System.ComponentModel.DataAnnotations;

namespace Integration.TradeXpress.Tenants;

public class TenantUpdateDto : IUpdateDto
{
    [Required]
    [StringLength(64)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Şirket grafı (şube→kasa dahil) — <c>Id</c> + <c>IsDeleted</c> ile DIFF'lenir.
    ///
    /// <para><b>BOŞ LİSTE = "graf gönderilmedi" demektir, "tüm şirketleri sil" DEĞİL.</b> Alan eklenmeden önce
    /// tenant güncellemesi yalnız adı yazıyordu; sadece adı değiştiren bir çağrı (ya da eski bir istemci) bu
    /// listeyi doldurmaz. Boş listeyi silme emri saymak, tek bir alan güncellemesiyle tenant'ın tüm org ağacını
    /// yok etmek olurdu.</para>
    ///
    /// <para>Aynı gerekçenin devamı: listede BULUNMAYAN mevcut şirkete de dokunulmaz. Silme YALNIZ açık
    /// <c>IsDeleted</c> işaretiyle olur.</para></summary>
    public List<CompanyGraphDto> Companies { get; set; } = new();
}
