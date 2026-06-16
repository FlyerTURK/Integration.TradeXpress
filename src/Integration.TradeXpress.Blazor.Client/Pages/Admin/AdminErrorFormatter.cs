using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Integration.TradeXpress.Blazor.Client.Pages.Admin;

/// <summary>
/// Identity (User/Role) edit formları için hata mesajı çıkarıcı.
/// Server-side direkt ABP app-service çağrısında fırlatılan
/// <see cref="Volo.Abp.Validation.AbpValidationException"/> hatalarını
/// (ValidationErrors → ErrorMessage) ve HTTP remote hatalarını
/// (Error.ValidationErrors → Message) tek yerde okunur metne çevirir.
/// </summary>
public static class AdminErrorFormatter
{
    public static string Extract(Exception ex)
    {
        // 1) En içteki anlamlı exception'ı bul (UserFriendly / Validation iç içe gelebilir)
        var current = ex;
        while (current != null)
        {
            // a) AbpValidationException — server-side direkt çağrı
            if (current is Volo.Abp.Validation.AbpValidationException ve && ve.ValidationErrors?.Count > 0)
            {
                var msgs = ve.ValidationErrors
                    .Select(e => e.ErrorMessage)
                    .Where(m => !string.IsNullOrWhiteSpace(m))
                    .Distinct()
                    .ToList();
                if (msgs.Count > 0) return string.Join("\n", msgs);
            }

            // b) ABP UserFriendly / Business exception — Message zaten anlamlı
            if (current is Volo.Abp.AbpException &&
                !string.IsNullOrWhiteSpace(current.Message) &&
                !current.Message.Contains("not valid", StringComparison.OrdinalIgnoreCase))
            {
                return current.Message;
            }

            // c) HTTP remote hatası — reflection ile Error.ValidationErrors
            var remote = TryRemote(current);
            if (remote != null) return remote;

            current = current.InnerException;
        }

        return ex.Message;
    }

    private static string? TryRemote(Exception ex)
    {
        try
        {
            var errorProp = ex.GetType().GetProperty("Error");
            if (errorProp?.GetValue(ex) is not { } error) return null;

            var veProp = error.GetType().GetProperty("ValidationErrors");
            if (veProp?.GetValue(error) is IEnumerable errors)
            {
                var msgs = new List<string>();
                foreach (var e in errors)
                {
                    var msgProp = e?.GetType().GetProperty("Message");
                    if (msgProp?.GetValue(e) is string msg && !string.IsNullOrWhiteSpace(msg))
                        msgs.Add(msg);
                }
                if (msgs.Count > 0) return string.Join("\n", msgs.Distinct());
            }

            var detailProp = error.GetType().GetProperty("Message");
            if (detailProp?.GetValue(error) is string detail &&
                !string.IsNullOrWhiteSpace(detail) &&
                !detail.Contains("not valid", StringComparison.OrdinalIgnoreCase))
                return detail;
        }
        catch { /* reflection hatası → null */ }
        return null;
    }
}
