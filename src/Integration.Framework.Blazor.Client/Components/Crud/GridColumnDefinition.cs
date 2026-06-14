namespace Integration.Framework.Blazor.Client.Components.Crud;

public class GridColumnDefinition
{
    public string? FieldName { get; set; }
    public string? Caption { get; set; }
    public string? Width { get; set; }
    public string? DisplayFormat { get; set; }
    public bool Visible { get; set; } = true;
}
