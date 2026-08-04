using System;
using System.Collections.Generic;

namespace Integration.TradeXpress.N11Products;

/// <summary>
/// N11 REST YAZMA uçlarının (<c>product-create</c> · <c>product-update</c> · <c>price-stock-update</c>) DÖNÜŞÜ.
/// <para>
/// <b>Bu bir SONUÇ değil, MAKBUZDUR.</b> Üç uç da asenkrondur: yanıt gövdesi
/// <c>{ "id": 1092, "type": "PRODUCT_UPDATE", "status": "IN_QUEUE", "reasons": [...] }</c> şeklindedir ve
/// yalnız "istek kuyruğa alındı" der. Ürünün gerçekten yüklendiğini/güncellendiğini <b>yalnız</b>
/// <see cref="N11TaskPoller"/> söyler. Yerel kaydı bu makbuza bakarak "senkron edildi" işaretlemek YANLIŞTIR —
/// doğrusu "kuyruğa girdi" + <see cref="TaskId"/> saklamaktır.
/// </para>
/// </summary>
/// <param name="TaskId">Yanıttaki <c>id</c> alanı — <see cref="N11TaskPoller.QueryAsync"/>'in adresleme anahtarı.</param>
/// <param name="RawStatus">Yanıttaki ham <c>status</c> (<c>IN_QUEUE</c> / <c>REJECT</c>). Yorumu için
/// <see cref="N11TaskStates.Parse"/>. <c>REJECT</c> ise veri seti hiç yüklenmemiştir ve poller'ı beklemek anlamsızdır.</param>
public sealed record N11TaskSubmission(string TaskId, string RawStatus);

/// <summary>N11 task'ının yaşam döngüsü — doküman: <c>PROCESSED</c> = tamamlandı · <c>IN_QUEUE</c> = işleniyor · <c>REJECT</c> = işlenmedi.</summary>
public enum N11TaskState
{
    /// <summary><c>IN_QUEUE</c> — task kuyrukta, sonuç HENÜZ BELLİ DEĞİL. Tekrar sorgulanmalı; başarı sayılmaz.</summary>
    InQueue,

    /// <summary><c>PROCESSED</c> — task işlendi. <b>SKU bazında başarı GARANTİ DEĞİL</b>: tek tek
    /// <see cref="N11TaskItemResult.Success"/> okunmalıdır (kısmi başarı normaldir).</summary>
    Processed,

    /// <summary><c>REJECT</c> — veri seti hiç işlenmedi; sebep <see cref="N11TaskResult.RejectReason"/>'da.</summary>
    Rejected,

    /// <summary>N11 belgelenmemiş bir statü döndürdü. <b>Başarı sayılmaz</b> — çağıran bunu hata gibi ele almalıdır.</summary>
    Unknown,
}

/// <summary>
/// Task içindeki TEK BİR SKU'nun sonucu (<c>skus.content[]</c> öğesi).
/// </summary>
/// <param name="ItemCode">N11'in <c>itemCode</c> alanı — <b>bizim satıcı stok kodumuzdur</b> (<c>stockCode</c>),
/// N11 ürün id'si değil. Yerel eşleme bu anahtarla yapılır.</param>
/// <param name="Success">Ham <c>status</c> alanı <c>SUCCESS</c> mi (N11 <c>Fail</c> değerini de döndürür).</param>
/// <param name="Reason">N11'in gerekçe metni (<c>reasons[]</c> birleştirilmiş). Başarıda da dolu gelebilir
/// (<i>"Başarıyla tamamlandı."</i>), yani <b>dolu olması hata anlamına GELMEZ</b>.</param>
public sealed record N11TaskItemResult(string ItemCode, bool Success, string? Reason);

/// <summary>
/// Bir N11 task'ının TAM sonucu — tüm sayfalar birleştirilmiş hâli (bkz. <see cref="N11TaskPoller"/>).
/// </summary>
/// <param name="State">Task'ın kendi statüsü. <see cref="N11TaskState.Processed"/> olsa bile
/// <paramref name="Items"/> tek tek okunmalıdır.</param>
/// <param name="Items">SKU bazlı sonuçlar. <see cref="N11TaskState.Rejected"/> durumunda BOŞ olabilir.</param>
/// <param name="RejectReason">Task seviyesindeki gerekçe (özellikle <see cref="N11TaskState.Rejected"/> için).</param>
public sealed record N11TaskResult(N11TaskState State, IReadOnlyList<N11TaskItemResult> Items, string? RejectReason);

/// <summary>
/// N11'in ham statü dizgilerini tek yerden çözer — <b>SSOT</b>. Hem yazma uçları (makbuzdaki <c>status</c>)
/// hem poller (task'ın <c>status</c>'ü) aynı sözlüğü kullansın diye ayrı tutuldu; dizgiler koda dağılmaz.
/// </summary>
public static class N11TaskStates
{
    /// <summary>Task kuyrukta bekliyor.</summary>
    public const string InQueue = "IN_QUEUE";

    /// <summary>Task işlendi (SKU bazlı sonuç okunmalı).</summary>
    public const string Processed = "PROCESSED";

    /// <summary>Task hiç işlenmedi.</summary>
    public const string Reject = "REJECT";

    /// <summary>SKU seviyesinde başarı değeri (karşıtı: <c>Fail</c>).</summary>
    public const string ItemSuccess = "SUCCESS";

    /// <summary>
    /// Ham statüyü enum'a çevirir. Bilinmeyen/boş değer <see cref="N11TaskState.Unknown"/> döner —
    /// <b>sessizce "başarılı" varsayılmaz</b>. Karşılaştırma büyük/küçük harf duyarsız ve
    /// <c>Trim</c>'lidir (N11 bazı statü alanlarını baştaki boşlukla döndürüyor — doküman denetimi f.8).
    /// <c>REJECTED</c> yazımı da savunma amaçlı karşılanır.
    /// </summary>
    public static N11TaskState Parse(string? rawStatus)
    {
        var value = rawStatus?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return N11TaskState.Unknown;
        }

        if (string.Equals(value, Processed, StringComparison.OrdinalIgnoreCase))
        {
            return N11TaskState.Processed;
        }

        if (string.Equals(value, InQueue, StringComparison.OrdinalIgnoreCase))
        {
            return N11TaskState.InQueue;
        }

        if (string.Equals(value, Reject, StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "REJECTED", StringComparison.OrdinalIgnoreCase))
        {
            return N11TaskState.Rejected;
        }

        return N11TaskState.Unknown;
    }

    /// <summary>SKU seviyesindeki ham statüyü değerlendirir. <b>Yalnız <c>SUCCESS</c> başarıdır</b> —
    /// bilinmeyen değer başarısız sayılır (fail-safe).</summary>
    public static bool IsItemSuccess(string? rawStatus)
    {
        return string.Equals(rawStatus?.Trim(), ItemSuccess, StringComparison.OrdinalIgnoreCase);
    }
}
