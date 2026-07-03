namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// Çeşni stoğu özeti — takoz GİRİŞ satırlarında biriken numune (AssayAmount) havuzu MİNUS çeşni çıkışları.
/// Milyemler legacy <c>Cesni.cs</c> paritesiyle TÜRETİLİR: AuMilyem = Has / Miktar (ağırlıklı ortalama).
/// Çeşni panelinin açılış ön-doldurma kaynağı.
/// </summary>
public class AssayStockDto
{
    /// <summary>Kalan çeşni miktarı (gram): Σ takoz-giriş AssayAmount − Σ çeşni-çıkış Amount.</summary>
    public decimal Amount { get; set; }

    /// <summary>Kalan HAS: Σ(AssayAmount × altın milyemi) − Σ(çıkış Amount × Factor).</summary>
    public decimal Has { get; set; }

    /// <summary>Kalan GUM: Σ(AssayAmount × gümüş milyemi) − Σ(çıkış Amount × SilverFactor).</summary>
    public decimal Gum { get; set; }

    /// <summary>Ağırlıklı ortalama altın milyemi (Has / Miktar; miktar 0 ise 0).</summary>
    public decimal AuMilyem { get; set; }

    /// <summary>Ağırlıklı ortalama gümüş milyemi (Gum / Miktar; miktar 0 ise 0).</summary>
    public decimal AgMilyem { get; set; }
}
