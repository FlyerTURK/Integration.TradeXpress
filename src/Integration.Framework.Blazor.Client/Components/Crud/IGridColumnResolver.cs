namespace Integration.Framework.Blazor.Client.Components.Crud;

/// <summary>
/// Bir List DTO tipinin genel (public) özelliklerinden grid kolon tanımlarını üretir.
/// Sayfalar elle <c>GridColumns</c> markup'ı yazmak yerine bunu kullanıp
/// <c>CrudLayout.Columns</c> parametresini besleyebilir ("business object → ekran" hedefi).
/// </summary>
public interface IGridColumnResolver
{
    IReadOnlyList<GridColumnDefinition> Resolve<TListDto>();

    IReadOnlyList<GridColumnDefinition> Resolve(Type listDtoType);
}
