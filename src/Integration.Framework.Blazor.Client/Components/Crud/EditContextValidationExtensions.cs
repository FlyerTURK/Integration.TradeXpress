using Integration.Framework.Blazor.Client.Services.Base;
using Microsoft.AspNetCore.Components.Forms;

namespace Integration.Framework.Blazor.Client.Components.Crud;

/// <summary>
/// Doğrulama sonucunu kullanıcıya DUYURAN ortak yardımcı. Kaydet'in sessizce hiçbir şey yapmaması
/// (butona basılır, form kapanmaz, sebep görünmez) tekrar eden bir kullanıcı şikâyeti — bu yüzden hata
/// mesajları inline işaretin YANINDA toast olarak da duyurulur.
/// <para>Tek kaynak: <c>DrillList</c> ve <c>ValueObjectEditPopup</c> AYNI bu yolu kullanır (kopya YOK).</para>
/// </summary>
public static class EditContextValidationExtensions
{
    /// <summary>Bağlamdaki tüm doğrulama mesajlarını tekilleştirip hata toast'ı olarak gösterir.
    /// Aynı mesaj birden çok alandan gelebildiği için <c>Distinct</c> şart (aksi halde toast yığılır).
    /// <para><paramref name="contextLabel"/> doluysa mesaj öneklenir ("Şube HQ: Kod alanı zorunludur.") —
    /// iç içe drill/popup'larda HANGİ kaydın alanı olduğu ancak böyle görünür (2026-08-01 bağlam-zinciri).</para></summary>
    public static void ShowValidationToasts(this EditContext? context, IUiInteractionService uiService, string? contextLabel = null)
    {
        if (context is null)
        {
            return;
        }

        foreach (var message in context.GetValidationMessages().Distinct())
        {
            uiService.ShowErrorToast(string.IsNullOrEmpty(contextLabel) ? message : $"{contextLabel}: {message}");
        }
    }

    /// <summary>İki bağlam etiketini " → " ile zincirler (boşları atlayarak) — DrillList/ValueObjectEditPopup
    /// cascade'inin ortak birleştiricisi: "Şirket FMS → Şube HQ → Adres".</summary>
    public static string? CombinePath(string? parent, string? own)
    {
        if (string.IsNullOrEmpty(parent))
        {
            return own;
        }

        if (string.IsNullOrEmpty(own))
        {
            return parent;
        }

        return $"{parent} → {own}";
    }
}
