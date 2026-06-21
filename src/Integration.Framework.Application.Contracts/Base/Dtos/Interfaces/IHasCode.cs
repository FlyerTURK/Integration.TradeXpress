namespace Integration.Framework.Base.Dtos.Interfaces;

/// <summary><c>Code</c> kimlik alanı taşıyan DTO'lar. Yapısal başlığın L2 (kimlik) değeri buradan
/// <b>explicit</b> okunur — reflection/sihirli "Code" property araması yok (fail-fast + test edilebilir + perf).</summary>
public interface IHasCode
{
    string? Code { get; }
}
