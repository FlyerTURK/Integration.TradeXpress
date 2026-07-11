using System;
using System.Collections.Generic;

namespace Integration.TradeXpress.Orders;

/// <summary>KANAL-AGNOSTİK uzak sipariş — ÇÖZÜLMÜŞ snapshot. Trendyol ve N11 istemcileri AYNI bu tipi üretir →
/// <see cref="OrderAppService"/> upsert'i tek gövdede (kanal-özel parse istemcinin içinde kalır). <see cref="RemoteOrderId"/>
/// idempotency anahtarıdır; alanlar yerel ürüne BAĞIMSIZ tam anlamlıdır (ürün-agnostik).</summary>
public sealed record RemoteOrder(
    string RemoteOrderId,
    string OrderNumber,
    DateTime OrderDate,
    string? RemoteStatus,
    string? CustomerName,
    decimal TotalAmount,
    string? CargoProvider,
    string? CargoTrackingNumber,
    IReadOnlyList<RemoteOrderLine> Lines);

/// <summary>Uzak sipariş satırı (kalem) — ürün-agnostik snapshot (yerel ürün olmasa da tam anlamlı).</summary>
public sealed record RemoteOrderLine(
    string? RemoteLineId,
    string? Barcode,
    string? StockCode,
    string ProductName,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    string? RemoteLineStatus);
