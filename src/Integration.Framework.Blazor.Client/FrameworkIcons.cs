namespace Integration.Framework.Blazor.Client;

/// <summary>
/// Framework bileşenlerinin STANDART ikon sınıfları — TEK KAYNAK (DRY). <c>custom-icon-*</c> CSS sınıfları
/// app'in <c>main.css</c>'inde tanımlı; Framework, app'in <c>TradeXpressIcons</c>'ına referans VEREMEZ
/// (proje yönü: app → framework), o yüzden framework'ün standart ikonları burada toplanır.
/// <para>Bileşenler bunu <b>default</b> alır; gerekirse ilgili parametreyle (ör. <c>EditIconCssClass</c>)
/// override edilir. Bir framework bileşenine ikon yazarken ham <c>"custom-icon-..."</c> literal'i TEKRAR
/// ETME — buraya sabit ekle, oradan kullan.</para>
/// </summary>
public static class FrameworkIcons
{
    public const string Edit    = "custom-icon-edit";
    public const string Add     = "custom-icon-add";
    public const string Search  = "custom-icon-search";
    public const string Info    = "custom-icon-info";
    /// <summary>Menüde "işaretli/aktif" durum göstergesi (DevExpress checked-item deseni: IconCssClass toggle).</summary>
    public const string Check   = "custom-icon-check";

    /// <summary>Onaylama/seçme eylemi — YEŞİL tik (fiş "Tamam" düğmesiyle AYNI ikon; kullanıcı kararı
    /// 2026-07-28: seçim onayı her yerde aynı görünsün).</summary>
    public const string Confirm = "custom-icon-tick-green";

    // Lookup combo butonları — RENKLİ (xaf SVG kendi renkleriyle çizilir; aksiyon ikonlarından farklı olarak
    // tek-renk/currentColor DEĞİL). Kullanıcı kararı: lookup'ın renkli görünümü STANDART. main.css'te tanımlı.
    public const string LookupEdit = "xaf-edit-icon";
    public const string LookupAdd  = "xaf-new-icon";

    // Giriş paneli (EntryPanelShell) butonları
    public const string Save = "custom-icon-save";
    public const string Back = "custom-icon-back";

    // Pencere kontrolleri (CrudEditView / EditShell)
    public const string Minimize       = "custom-icon-minimize";
    public const string MinimizeCorner = "custom-icon-minimize-corner";
    public const string RestoreMin     = "custom-icon-restore-min";
    public const string Maximize       = "custom-icon-maximize";
}
