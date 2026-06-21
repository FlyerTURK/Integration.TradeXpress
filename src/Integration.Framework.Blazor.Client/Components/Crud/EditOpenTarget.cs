namespace Integration.Framework.Blazor.Client.Components.Crud;

/// <summary>
/// Liste sayfasının edit formunu NEREDE açacağı. <see cref="CrudPageBase{TGetDto,TListDto,TKey,TListRequestDto,TCreateInput,TUpdateInput}.EditOpenTarget"/>
/// ile sayfa bazında seçilir (varsayılan Popup).
/// </summary>
public enum EditOpenTarget
{
    /// <summary>Modal popup (varsayılan) — IViewOpener ile açılır.</summary>
    Popup,

    /// <summary>MDI sekmesi — edit page'in route'u IMdiTabOpener ile sekmede açılır.
    /// Uygulama MDI sağlamıyorsa (IMdiTabOpener kayıtlı değilse) popup'a düşer.</summary>
    MdiTab
}
