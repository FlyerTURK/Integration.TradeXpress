namespace Integration.Framework.Blazor.Client.Components.Crud;

/// <summary>
/// DrillList (in-memory master-detail) sınırları. DrillList tüm child set'i belleğe çeker; bu yüzden
/// bir tavan zorunludur. Tavana ULAŞILIRSA çağıran, kullanıcıya GÖRÜNÜR uyarı vermelidir — sessiz
/// veri kesintisi yasak. Bu pattern yalnızca küçük & bounded child koleksiyonları için tasarlandı;
/// tavanın production'da fiilen tetiklenmesi delta/server-paged modele geçiş tetikleyicisidir.
/// </summary>
public static class DrillConsts
{
    /// <summary>In-memory drill'in çekeceği azami child sayısı. Aşılırsa sesli uyarı verilir.</summary>
    public const int MaxInMemoryChildren = 1000;

    /// <summary>Tüm DrillList'lerin STANDART grid yüksekliği (sabit). Form başına ayrı px verilmez;
    /// her drill bunu kullanır. Tek yerden değiştir → her yere yansır. DrillList.Height ile yine de
    /// gerekirse kullanım başına ezilebilir. Grid bu yüksekliği doldurur, fazla satır içeride kayar.</summary>
    public const string DefaultGridHeight = "180px";
}
