using System.Threading.Tasks;

namespace Integration.Framework.Data;

/// <summary>
/// Başarısız <c>SaveChanges</c> sonrası AYNI unit-of-work içinde güvenli retry hazırlığı.
/// ABP her SaveChanges öncesi Modified/Deleted entity'lerin ConcurrencyStamp'ini döndürür
/// (original←mevcut, mevcut←yeni); SaveChanges başarısız olunca EF veritabanını savepoint'e
/// geri sarar ama ABP'nin bellekteki rotasyonu geri SARILMAZ. Retry'daki ikinci rotasyon
/// artık veritabanında olmayan stamp'i WHERE koşuluna koyar → 0 satır → sahte
/// DbUpdateConcurrencyException. Bu soyutlama rotasyonu original değerine geri sararak
/// bellek/veritabanı hizasını kurar. EF tipleri alt katmana sızmasın diye implementasyon
/// EntityFrameworkCore katmanında yaşar; tüketiciler (Application) bu soyutlamayı kullanır.
/// </summary>
public interface IConcurrencyStampRestorer
{
    /// <summary>Aktif DbContext'teki Modified/Deleted concurrency-stamp'li entity'lerin
    /// stamp'ini original (veritabanıyla hizalı) değerine geri sarar. Başarısız SaveChanges'i
    /// AYNI transaction içinde tekrar denemeden ÖNCE çağrılır; idempotenttir.</summary>
    Task RestoreRotatedStampsAsync();
}
