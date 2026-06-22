using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Volo.Abp.DependencyInjection;

namespace Integration.Framework.Blazor.Client.Components.Crud;

/// <summary>
/// Reflection tabanlı varsayılan <see cref="IGridColumnResolver"/>.
/// Kurallar: "Id" özelliği ve <c>[ScaffoldColumn(false)]</c> ile işaretliler atlanır;
/// caption için sırasıyla <c>[Display(Name)]</c>, <c>[DisplayName]</c>, ardından özellik adı kullanılır.
/// </summary>
public class GridColumnResolver : IGridColumnResolver, ISingletonDependency
{
    public IReadOnlyList<GridColumnDefinition> Resolve<TListDto>()
        => Resolve(typeof(TListDto));

    public IReadOnlyList<GridColumnDefinition> Resolve(Type listDtoType)
    {
        return listDtoType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(IsColumnCandidate)
            .Select(p => new GridColumnDefinition
            {
                FieldName = p.Name,
                Caption = ResolveCaption(p),
                Visible = true
            })
            .ToList();
    }

    private static bool IsColumnCandidate(PropertyInfo property)
    {
        if (property.GetIndexParameters().Length > 0)
        {
            return false;
        }

        if (string.Equals(property.Name, "Id", StringComparison.Ordinal))
        {
            return false;
        }

        // IsActive grid'de kolon DEĞİL — toolbar'daki switch filtresiyle yönetilir (StatusCell kuralı).
        if (string.Equals(property.Name, "IsActive", StringComparison.Ordinal))
        {
            return false;
        }

        var scaffold = property.GetCustomAttribute<ScaffoldColumnAttribute>();
        if (scaffold is { Scaffold: false })
        {
            return false;
        }

        return true;
    }

    private static string ResolveCaption(PropertyInfo property)
    {
        var display = property.GetCustomAttribute<DisplayAttribute>();
        if (!string.IsNullOrWhiteSpace(display?.Name))
        {
            return display!.Name!;
        }

        var displayName = property.GetCustomAttribute<DisplayNameAttribute>();
        if (!string.IsNullOrWhiteSpace(displayName?.DisplayName))
        {
            return displayName!.DisplayName;
        }

        return property.Name;
    }
}
