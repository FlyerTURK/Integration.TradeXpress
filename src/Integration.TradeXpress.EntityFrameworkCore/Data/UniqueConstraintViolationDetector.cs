using System;
using Integration.Framework.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Data;

/// <summary>
/// <see cref="IUniqueConstraintViolationDetector"/> implementasyonu — sağlayıcı hata kodundan
/// tip-güvenli sınıflandırma. SQL Server: 2601 (unique index) / 2627 (unique constraint).
/// Sqlite (testler in-memory Sqlite'ta koşar): primary 19 (SQLITE_CONSTRAINT) + extended
/// 2067 (UNIQUE) / 1555 (PK UNIQUE) — primary 19 tek başına FK/NOT NULL ihlallerini de
/// kapsadığından extended kodla daraltılır.
/// NOT: Framework'te EntityFrameworkCore projesi yok; SqlClient referansı taşıyan en merkezi
/// katman burası. Framework.EntityFrameworkCore doğarsa bu sınıf oraya taşınır.
/// </summary>
public class UniqueConstraintViolationDetector : IUniqueConstraintViolationDetector, ISingletonDependency
{
    private const int SqlServerUniqueIndexError      = 2601;
    private const int SqlServerUniqueConstraintError = 2627;
    private const int SqliteConstraintError          = 19;   // SQLITE_CONSTRAINT (primary)
    private const int SqliteConstraintUnique         = 2067; // SQLITE_CONSTRAINT_UNIQUE
    private const int SqliteConstraintPrimaryKey     = 1555; // SQLITE_CONSTRAINT_PRIMARYKEY

    public bool IsUniqueConstraintViolation(Exception exception, string? constraintNameHint = null)
    {
        for (var e = exception; e != null; e = e.InnerException)
        {
            if (!IsProviderUniqueViolation(e))
            {
                continue;
            }

            // Birincil sınıflandırma hata kodu; hint verilmişse mesajda index/kolon adı
            // İKİNCİL (daraltıcı) kontrol olarak aranır — başka bir unique index'in
            // ihlali yanlışlıkla bu ihlale yorulmasın.
            return constraintNameHint == null
                || e.Message.Contains(constraintNameHint, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool IsProviderUniqueViolation(Exception exception)
    {
        switch (exception)
        {
            case SqlException sqlException:
                return sqlException.Number is SqlServerUniqueIndexError or SqlServerUniqueConstraintError;

            case SqliteException sqliteException:
                return sqliteException.SqliteErrorCode == SqliteConstraintError
                    && sqliteException.SqliteExtendedErrorCode
                        is SqliteConstraintUnique or SqliteConstraintPrimaryKey;

            default:
                return false;
        }
    }
}
