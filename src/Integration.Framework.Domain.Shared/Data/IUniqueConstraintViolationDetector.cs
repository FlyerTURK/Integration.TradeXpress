using System;

namespace Integration.Framework.Data;

/// <summary>
/// Veritabanı sağlayıcısına özgü unique index/constraint ihlalini HATA KODUNDAN tanır
/// (mesaj metnine bakmak collation/lokalizasyon/index-adı değişiminde kırılgandır).
/// Sağlayıcı tipleri (SqlClient/Sqlite) alt katmanlara sızmasın diye implementasyon
/// EntityFrameworkCore katmanında yaşar; tüketiciler (Application) bu soyutlamayı kullanır.
/// </summary>
public interface IUniqueConstraintViolationDetector
{
    /// <summary>
    /// <paramref name="exception"/> zincirinde (InnerException dahil) sağlayıcıya özgü
    /// unique ihlali var mı? <paramref name="constraintNameHint"/> verilirse mesajda
    /// index/kolon adı da aranır — İKİNCİL (daraltıcı) kontrol; birincil sınıflandırma
    /// daima sağlayıcının hata kodudur.
    /// </summary>
    bool IsUniqueConstraintViolation(Exception exception, string? constraintNameHint = null);
}
