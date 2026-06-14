namespace Integration.Framework.Blazor.Client.Components.Crud;

public interface IViewModel<TKey>
{
    TKey Id { get; set; }
}
