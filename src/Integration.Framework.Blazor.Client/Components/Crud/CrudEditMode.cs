namespace Integration.Framework.Blazor.Client.Components.Crud;

/// <summary>
/// Bir CRUD liste sayfasının edit formunu nerede açacağını belirler — MERKEZİ standart.
/// <para><see cref="Popup"/>: yerleşik <c>CrudEditModal</c> popup'ı (varsayılan; tüm klasik ekranlar).</para>
/// <para><see cref="Tab"/>: edit formu ayrı bir MDI sekmesinde açılır (routable edit sayfası gerektirir;
/// list page <c>NewTabUrl</c>/<c>EditTabUrl</c> verir).</para>
/// </summary>
public enum CrudEditMode
{
    Popup,
    Tab
}
