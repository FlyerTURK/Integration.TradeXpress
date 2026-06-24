namespace Integration.TradeXpress.Authorization;

/// <summary>Kapsamlı (scoped) grant yönü. Çözümlemede EN SPESİFİK kapsam kazanır (her iki yönde):
/// üst Grant + alt Deny → o alt-ağaçta red; üst Deny + alt Grant → kabul.</summary>
public enum ScopedGrantMode
{
    Grant = 0,
    Deny = 1
}
