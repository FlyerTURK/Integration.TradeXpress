using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;

namespace Integration.Framework.Blazor.Client.Services.Base;

/// <summary>
/// Popup chrome'una (GlobalPopupHost) başlık içeriği sağlama sözleşmesi — cascade ile verilir.
/// Hosted edit form, POPUP modunda kendi yapısal başlığını BODY'DE çizmek yerine buraya verir →
/// chrome header'ı dolar (boş band + çift başlık olmaz), DevExpress'in NATIVE close X'i korunur.
/// Tab/standalone modda bu cascade YOKTUR → form başlığı body'de çizer (DrillList'in kendi popup'ı
/// zaten kendi HeaderContentTemplate'ini kullanır; bu yalnız GlobalPopupHost yolu içindir).
/// </summary>
public interface IPopupChrome
{
    /// <summary>Chrome header içeriğini ayarlar (null → temizle). Reaktif: form dirty/başlık değişince yeniden çağrılır.</summary>
    void SetHeader(RenderFragment? header);

    /// <summary>Popup kapanışı (native X/Escape/dış-tık) ÖNCESİ çağrılacak guard'ı kaydeder (null → temizle).
    /// false dönerse popup kapanmaz (ör. kirli formda "İptal"). Form, kendi CanLeaveAsync'ini buraya verir.</summary>
    void SetCloseGuard(Func<Task<bool>>? guard);
}
