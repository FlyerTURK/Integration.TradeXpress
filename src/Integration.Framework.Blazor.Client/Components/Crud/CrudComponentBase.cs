using Volo.Abp.AspNetCore.Components;

namespace Integration.Framework.Blazor.Client.Components.Crud;

/// <summary>
/// Framework CRUD bileşenlerinin ortak tabanı. App-agnostik oldukları için belirli bir resource'a
/// derleme-zamanı bağımlılık kuramazlar; bu yüzden uygulama, başlangıçta kendi localization
/// resource tipini <see cref="DefaultLocalizationResource"/> statiğine yazar ve biz onu
/// <b>constructor'da</b> <see cref="AbpComponentBase.LocalizationResource"/>'a atarız.
///
/// <para>Neden constructor: AbpComponentBase <c>L</c>'yi erken kuruyor; OnInitialized'da set etmek
/// geç kalıyor ve L çeviri bulamayıp ham anahtarı ("New", "Save"...) döndürüyordu — yarı-İngilizce
/// UI'nın sebebi buydu. (TenantListPage de resource'u ctor'da set ettiği için Türkçe çalışıyor.)</para>
/// </summary>
public abstract class CrudComponentBase : AbpComponentBase
{
    /// <summary>Uygulama başlangıçta kendi default resource tipini buraya yazar (framework referans kurmadan).</summary>
    public static Type? DefaultLocalizationResource { get; set; }

    protected CrudComponentBase()
    {
        if (DefaultLocalizationResource != null)
        {
            LocalizationResource = DefaultLocalizationResource;
        }
    }
}
