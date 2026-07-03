using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Integration.TradeXpress.Conventions;

/// <summary>
/// Kaynak-dosya tarayan convention testleri için ORTAK altyapı (DRY). Reflection değil, DİSK okur:
/// repo kökünü bulur, <c>src/**</c> altındaki dosyaları (obj/bin hariç) tek geçişte enumere eder,
/// yolları repo-köküne göreli + forward-slash normalize eder (allow-list literalleriyle birebir eşleşsin).
/// <para>Repo kökü stratejisi: test bin klasöründen (AppContext.BaseDirectory) YUKARI yürüyüp
/// <c>Integration.TradeXpress.slnx</c> içeren dizini bulur — CI/lokal fark etmeksizin deterministik.</para>
/// </summary>
internal static class ConventionSource
{
    // Repo kök işareti: solution dosyası. Tek-sefer bulunur (Lazy), tekrar disk taraması yok.
    private const string RootMarker = "Integration.TradeXpress.slnx";

    private static readonly Lazy<string> RepoRootLazy = new(FindRepoRoot);

    /// <summary>Repo kök dizini (mutlak). <c>src</c> ve <c>test</c> bunun altındadır.</summary>
    public static string RepoRoot => RepoRootLazy.Value;

    /// <summary>src kök dizini (mutlak).</summary>
    public static string SrcRoot => Path.Combine(RepoRoot, "src");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, RootMarker)))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Repo kökü bulunamadı: '{RootMarker}' içeren dizin yok " +
            $"(başlangıç: {AppContext.BaseDirectory}). Convention testleri kaynak tarayamaz.");
    }

    /// <summary>
    /// src altındaki verilen desendeki tüm dosyalar; obj/bin ELENİR (üretilmiş kod taranmaz).
    /// Tek geçiş — deterministik ve hızlı.
    /// </summary>
    public static IEnumerable<string> EnumerateSource(string searchPattern) => Directory
        .EnumerateFiles(SrcRoot, searchPattern, SearchOption.AllDirectories)
        .Where(p => !IsGenerated(p));

    private static bool IsGenerated(string fullPath)
    {
        // Yol ayırıcıdan bağımsız: normalize edilmiş yolda /obj/ veya /bin/ segmenti var mı?
        var norm = Normalize(fullPath);
        return norm.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || norm.Contains("/bin/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Mutlak yolu repo-köküne göreli + forward-slash yapar (allow-list literalleriyle eşleşir).</summary>
    public static string RelativePath(string fullPath) =>
        Normalize(Path.GetRelativePath(RepoRoot, fullPath));

    private static string Normalize(string path) => path.Replace('\\', '/');
}
