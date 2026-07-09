using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.SalesChannels.Variants;

/// <summary>
/// Saf orkestratör karakterizasyonu (S2, 2026-07-09) — <see cref="VariantSetReconciler"/>'ın politika
/// sözleşmesini kilitler: orphan → remove, eksik → add, eşleşen → HİÇBİR callback (kullanıcı verisi korunur),
/// önce silmeler sonra eklemeler. S4 (N11) ve S6 (Trendyol) bu sözleşmeye bağlanacak — testi gevşetme.
/// </summary>
public class VariantSetReconcilerTests
{
    /// <summary>Test mevcut-satırı — anahtar + kimlik (silinenin HANGİSİ olduğunu doğrulamak için).</summary>
    private sealed record ExistingRow(string Key, int Id);

    [Fact]
    public async Task Missing_targets_are_added_in_target_order_when_nothing_exists()
    {
        var added = new List<string>();
        var removed = new List<ExistingRow>();

        await ReconcileAsync(
            targetKeys: new[] { "B", "A", "C" },
            existing: Array.Empty<ExistingRow>(),
            added, removed);

        added.ShouldBe(new[] { "B", "A", "C" });   // hedef GİRİŞ sırası korunur (sıralama yok)
        removed.ShouldBeEmpty();
    }

    [Fact]
    public async Task Orphans_are_removed_when_target_is_empty()
    {
        var added = new List<string>();
        var removed = new List<ExistingRow>();
        var existing = new[] { new ExistingRow("A", 1), new ExistingRow("B", 2) };

        await ReconcileAsync(Array.Empty<string>(), existing, added, removed);

        added.ShouldBeEmpty();
        removed.ShouldBe(existing);   // mevcut giriş sırasıyla
    }

    [Fact]
    public async Task Matched_keys_get_no_callback_orphans_removed_missing_added()
    {
        var added = new List<string>();
        var removed = new List<ExistingRow>();
        var existing = new[]
        {
            new ExistingRow("KEEP-1", 1),
            new ExistingRow("ORPHAN", 2),
            new ExistingRow("KEEP-2", 3),
        };

        await ReconcileAsync(new[] { "KEEP-1", "KEEP-2", "NEW-1", "NEW-2" }, existing, added, removed);

        removed.ShouldBe(new[] { existing[1] });          // yalnız orphan silinir
        added.ShouldBe(new[] { "NEW-1", "NEW-2" });       // yalnız eksikler eklenir
        // KEEP-1/KEEP-2 hiçbir callback almadı → kullanıcı override/reçete verisi dokunulmaz kaldı.
    }

    [Fact]
    public async Task Duplicate_target_keys_produce_single_add()
    {
        var added = new List<string>();
        var removed = new List<ExistingRow>();

        await ReconcileAsync(new[] { "A", "A", "B" }, Array.Empty<ExistingRow>(), added, removed);

        added.ShouldBe(new[] { "A", "B" });
    }

    [Fact]
    public async Task All_removes_happen_before_any_add()
    {
        var log = new List<string>();
        var existing = new[] { new ExistingRow("ORPHAN-1", 1), new ExistingRow("ORPHAN-2", 2) };

        await VariantSetReconciler.ReconcileAsync(
            targetKeys: new[] { "NEW-1", "NEW-2" },
            existingItems: existing,
            keySelector: e => e.Key,
            removeAsync: e =>
            {
                log.Add($"remove:{e.Key}");
                return Task.CompletedTask;
            },
            addAsync: key =>
            {
                log.Add($"add:{key}");
                return Task.CompletedTask;
            });

        log.ShouldBe(new[] { "remove:ORPHAN-1", "remove:ORPHAN-2", "add:NEW-1", "add:NEW-2" });
    }

    [Fact]
    public async Task Identical_sets_produce_no_callbacks()
    {
        var added = new List<string>();
        var removed = new List<ExistingRow>();
        var existing = new[] { new ExistingRow("A", 1), new ExistingRow("B", 2) };

        await ReconcileAsync(new[] { "A", "B" }, existing, added, removed);

        added.ShouldBeEmpty();
        removed.ShouldBeEmpty();
    }

    // ── Yardımcı — callback'leri listelere toplayan kısa sarmalayıcı ────────────────────────────────

    private static async Task ReconcileAsync(
        IReadOnlyList<string> targetKeys,
        IReadOnlyList<ExistingRow> existing,
        List<string> added,
        List<ExistingRow> removed)
    {
        await VariantSetReconciler.ReconcileAsync(
            targetKeys,
            existing,
            keySelector: e => e.Key,
            removeAsync: e =>
            {
                removed.Add(e);
                return Task.CompletedTask;
            },
            addAsync: key =>
            {
                added.Add(key);
                return Task.CompletedTask;
            });
    }
}
