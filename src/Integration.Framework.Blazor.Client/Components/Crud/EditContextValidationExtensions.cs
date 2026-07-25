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
    /// Aynı mesaj birden çok alandan gelebildiği için <c>Distinct</c> şart (aksi halde toast yığılır).</summary>
    public static void ShowValidationToasts(this EditContext? context, IUiInteractionService uiService)
    {
        if (context is null)
        {
            return;
        }

        foreach (var message in context.GetValidationMessages().Distinct())
        {
            uiService.ShowErrorToast(message);
        }
    }
}
