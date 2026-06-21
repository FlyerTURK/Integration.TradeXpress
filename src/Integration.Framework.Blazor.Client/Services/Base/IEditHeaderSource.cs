namespace Integration.Framework.Blazor.Client.Services.Base;

/// <summary>Edit formunun <b>yapısal başlığını</b> (3-satır: tür/kimlik/parent) sağlayan kaynak.
/// <see cref="ISplitEditActions"/>'tan AYRI (ISP) — "başlık" derdi "kaydet/sil/gez" aksiyon arayüzüne karışmaz.
/// CrudEditShell popup header'ı ve (gerekirse) diğer tüketiciler bundan okur. CrudEditComponentBase uygular.</summary>
public interface IEditHeaderSource
{
    TabHeaderData? EditHeader { get; }

    /// <summary>Kaydedilmemiş değişiklik (popup header "*"). Dirty TEK kaynak — tab tarafı MdiTab.IsDirty.</summary>
    bool IsDirty { get; }
}
