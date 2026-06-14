using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Integration.Framework.Blazor.Client.Services.Base;

/// <summary>
/// Bir öğe listesini tek tek işleyip her öğenin başarı/başarısızlığını
/// toplayan yardımcı. Tek bir öğenin hatası diğerlerini durdurmaz.
/// </summary>
public static class BatchOperation
{
    public static async Task<BatchOperationResult<T>> ExecuteAsync<T>(
        IEnumerable<T> items,
        Func<T, Task> action)
    {
        var result = new BatchOperationResult<T>();

        foreach (var item in items)
        {
            try
            {
                await action(item);
                result.Succeeded.Add(item);
            }
            catch (Exception ex)
            {
                result.Failed.Add((item, ex));
            }
        }

        return result;
    }
}
