namespace Integration.Framework.Base.Dtos;

/// <summary>
/// YAZILABİLİR aktiflik taşıyan DTO'lar — grid hücresinden doğrudan aktif/pasif yapılabilenler.
///
/// <para><b><see cref="IIsActive"/>'den farkı:</b> o bir MARKER'dır (özellik taşımaz) ve yalnız liste
/// toolbar'ının filtre switch'ini tetikler. Bu arayüz ise gerçek bir sözleşmedir: <c>StatusColumn</c> hücre
/// içi toggle'ı bunun üzerinden TİP-GÜVENLİ yazar — yansımaya (reflection) gerek kalmaz ve hangi listelerin
/// düzenlenebilir olduğu derleme zamanında görünür (2026-07-27 Hakan kararı).</para>
///
/// <para>Drill/graf DTO'ları uygular; kalıcılaşma ana formun Kaydet'inde olur (in-memory graf deseni).</para>
/// </summary>
public interface IHasIsActive
{
    bool IsActive { get; set; }
}
