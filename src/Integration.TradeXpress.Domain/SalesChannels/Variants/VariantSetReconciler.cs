namespace Integration.TradeXpress.SalesChannels.Variants;

/// <summary>
/// Anahtarlı varyant SETİ mutabakatının SAF orkestratörü — KANAL (N11/Trendyol) reconcile akışları için
/// (persistence delegate'lerle çağırana bırakılır). Politika:
/// <list type="bullet">
/// <item><b>Orphan</b> (mevcutta var, hedefte yok) → <c>removeAsync</c> (çağıran bağımlılarıyla — reçete
/// cascade vb. — siler).</item>
/// <item><b>Eksik</b> (hedefte var, mevcutta yok) → <c>addAsync</c> (çağıran satırı kurar; fırsatçı ERP
/// eşleştirmesi vb. çağıran politikasıdır).</item>
/// <item><b>Eşleşen</b> → DOKUNULMAZ (kullanıcı override/reçete verisi korunur) — hiçbir callback almaz.</item>
/// </list>
/// Sıra sözleşmesi (mevcut N11 akışının birebiri): önce TÜM silmeler (mevcut giriş sırasıyla), sonra
/// eklemeler (hedef giriş sırasıyla, tekrarlı hedef anahtar TEK ekleme). ERP <c>ProductVariantSynchronizer</c>
/// buna BAĞLANMAZ (bilinçli karar — analiz 1.3: çalışan+testli <c>ProductVariantSynchronizer</c> yerinde kalır).
/// </summary>
public static class VariantSetReconciler
{
    /// <summary>Mevcut anahtarlı kümeyi hedef anahtar kümesiyle mutabık kılar (orphan sil / eksik ekle /
    /// eşleşene dokunma). <typeparamref name="TKey"/> opak kombinasyon anahtarıdır (imza formatı
    /// tüketici-yerel); <typeparamref name="TExisting"/> çağıranın mevcut satır tipi (entity/header).</summary>
    /// <param name="targetKeys">Hedef kombinasyon anahtarları (kartezyenden türetilmiş; sırası ekleme sırasıdır).</param>
    /// <param name="existingItems">Mevcut satırlar (sırası silme sırasıdır).</param>
    /// <param name="keySelector">Mevcut satırın kombinasyon anahtarı (ör. CombinationSignature).</param>
    /// <param name="removeAsync">Orphan satırı bağımlılarıyla siler.</param>
    /// <param name="addAsync">Eksik anahtarın satırını kurar.</param>
    public static async Task ReconcileAsync<TKey, TExisting>(
        IReadOnlyList<TKey> targetKeys,
        IReadOnlyList<TExisting> existingItems,
        Func<TExisting, TKey> keySelector,
        Func<TExisting, Task> removeAsync,
        Func<TKey, Task> addAsync)
        where TKey : notnull
    {
        var targetSet = targetKeys.ToHashSet();

        // 1) Hedefte OLMAYAN mevcutlar → sil (çağıran bağımlılarıyla temizler).
        foreach (var existing in existingItems)
        {
            if (!targetSet.Contains(keySelector(existing)))
            {
                await removeAsync(existing);
            }
        }

        // 2) Mevcutta OLMAYAN hedefler → ekle (hedef sırasıyla; tekrarlı anahtar tek ekleme).
        var existingKeys = existingItems.Select(keySelector).ToHashSet();
        var added = new HashSet<TKey>();
        foreach (var key in targetKeys)
        {
            if (!existingKeys.Contains(key) && added.Add(key))
            {
                await addAsync(key);
            }
        }
    }
}
