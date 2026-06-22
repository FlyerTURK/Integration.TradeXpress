using System;
using System.ComponentModel.DataAnnotations;

namespace Integration.TradeXpress.Tenants;

/// <summary>
/// Yeni tenant onboarding'inde bellekte tutulan kullanıcı satırı (User DrillList öğesi). Tenant
/// kaydedilince her satır o tenant'ta gerçek kullanıcıya dönüşür. <see cref="IsAdmin"/> işaretli
/// satır tenant yöneticisi (admin rolü) olur — en az bir satır admin olmalı.
/// </summary>
public class TenantUserInput
{
    /// <summary>DrillList satır anahtarı (sunucuya gitmez; yalnız grid kimliği).</summary>
    public Guid ClientKey { get; set; } = Guid.NewGuid();

    [Required]
    [StringLength(256)]
    public string UserName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(128)]
    public string Password { get; set; } = string.Empty;

    // Şifre iki kez girilir (yanlış girip kurtaramama riskine karşı). Backend kullanmaz; yalnız UI doğrulaması.
    [Required]
    [Compare(nameof(Password))]
    public string PasswordConfirm { get; set; } = string.Empty;

    [StringLength(64)]
    public string? Name { get; set; }

    [StringLength(64)]
    public string? Surname { get; set; }

    /// <summary>Tenant yöneticisi mi? En az bir satır admin olmalı (admin rolü atanır).</summary>
    public bool IsAdmin { get; set; }
}
