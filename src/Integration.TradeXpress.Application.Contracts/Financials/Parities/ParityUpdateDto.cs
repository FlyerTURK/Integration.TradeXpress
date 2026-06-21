using Integration.Framework.Base.Dtos.Interfaces;

namespace Integration.TradeXpress.Financials.Parities;

/// <summary>
/// Parite güncelleme. Base/quote burada YOK — çift değişmezdir (entity immutable);
/// yön değiştirmek = yeni kayıt. Yalnız durum ve sıra güncellenir.
/// </summary>
public class ParityUpdateDto : IUpdateDto
{
    public bool IsActive { get; set; }

    public int DisplayOrder { get; set; }
}
