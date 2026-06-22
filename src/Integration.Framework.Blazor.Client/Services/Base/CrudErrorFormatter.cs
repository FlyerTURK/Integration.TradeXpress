using System.Collections;

namespace Integration.Framework.Blazor.Client.Services.Base;

/// <summary>
/// CRUD edit formlarında sunucu hatalarını okunur metne çevirir. ABP server-side çağrısında
/// fırlatılan <see cref="Volo.Abp.Validation.AbpValidationException"/>'ın (ValidationErrors →
/// ErrorMessage, ABP tarafından LOCALIZE edilmiş) ve HTTP remote hatalarının (Error.ValidationErrors
/// → Message) gerçek alan mesajlarını çıkarır. Aksi halde "Method arguments are not valid!" gibi
/// genel metin görünür.
/// </summary>
public static class CrudErrorFormatter
{
    /// <summary>
    /// Kullanıcıya gösterilebilir mesajı çıkarır. Yalnız <b>validation</b> ve <b>iş kuralı</b>
    /// (<see cref="Volo.Abp.IBusinessException"/>: BusinessException/UserFriendly/EntityNotFound) mesajları
    /// döner. Teknik hatalar (ObjectMapper "No object mapping…", NRE, vb. ham <c>AbpException</c>/Exception)
    /// kullanıcı-dostu DEĞİLDİR → <c>null</c> döner; çağıran genel bir mesaj gösterir (detay loglanır).
    /// </summary>
    public static string? Extract(Exception ex)
    {
        var current = ex;
        while (current != null)
        {
            // a) AbpValidationException — server-side direkt çağrı (mesajlar localize)
            if (current is Volo.Abp.Validation.AbpValidationException ve && ve.ValidationErrors?.Count > 0)
            {
                var msgs = ve.ValidationErrors
                    .Select(e => e.ErrorMessage)
                    .Where(m => !string.IsNullOrWhiteSpace(m))
                    .Distinct()
                    .ToList();
                if (msgs.Count > 0) return string.Join("\n", msgs);
            }

            // b) İş kuralı / kullanıcı-dostu exception — Message anlamlı. (Ham AbpException teknik olabilir →
            //    yalnız IBusinessException kabul; "No object mapping…" gibi teknik AbpException buraya GİRMEZ.)
            if (current is Volo.Abp.IBusinessException &&
                !string.IsNullOrWhiteSpace(current.Message) &&
                !current.Message.StartsWith("Exception of type", StringComparison.Ordinal) && // kodu olup lokalize edilmemiş → çevirisi ShowError.LocalizeErrorCode'da
                !current.Message.Contains("not valid", StringComparison.OrdinalIgnoreCase))
            {
                return current.Message;
            }

            // c) HTTP remote hatası — reflection ile Error.ValidationErrors
            var remote = TryRemote(current);
            if (remote != null) return remote;

            current = current.InnerException;
        }

        return null;   // kullanıcı-dostu mesaj yok → çağıran genel mesaj göstersin
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
