using System.Collections.Generic;
using Integration.TradeXpress.Orders;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Orders;

/// <summary>Sipariş ALICI (buyer) bilgisi — kompakt özet + ✎ → popup (ValueObjectEdit deseni; adres/kargo şablonuyla
/// TUTARLI). Özet kuralları (kullanıcı kararı): ad boşsa <see cref="BillingName"/>'e düşer; TCKN yalnız doluysa;
/// Vergi Dairesi → Vergi No (bu sırada) yalnız doluysa. Kayıt order form save'iyle (BuyerCorrection).</summary>
public partial class OrderBuyerEditFields
{
    [Parameter, EditorRequired] public OrderEditPartyDto Buyer { get; set; } = default!;

    /// <summary>Alıcı adı boşsa özet/placeholder için düşülecek Fatura Adresi adı (N11 bazen alıcı adı göndermez).</summary>
    [Parameter] public string? BillingName { get; set; }

    /// <summary>Özet item + popup başlığı ("Alıcı") — parent geçer.</summary>
    [Parameter] public string? Caption { get; set; }

    private bool _popupVisible;

    // ValueObjectEdit özeti — kurallı: ad (boşsa fatura adı) · TCKN (varsa) · Vergi Dairesi (varsa) · Vergi No (varsa).
    private string? BuyerSummary(OrderEditPartyDto party)
    {
        var parts = new List<string>();

        var name = FirstNonBlank(party.FullName, BillingName);
        if (name is not null)
        {
            parts.Add(name);
        }

        // TCKN yalnız doluysa.
        if (!string.IsNullOrWhiteSpace(party.TcId))
        {
            parts.Add($"{L["Order:Detail:TcId"].Value}: {party.TcId!.Trim()}");
        }

        // Vergi Dairesi ÖNCE, Vergi No SONRA — her biri yalnız doluysa.
        if (!string.IsNullOrWhiteSpace(party.TaxOffice))
        {
            parts.Add($"{L["Order:Detail:TaxOffice"].Value}: {party.TaxOffice!.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(party.TaxId))
        {
            parts.Add($"{L["Order:Detail:TaxId"].Value}: {party.TaxId!.Trim()}");
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    // Alıcı "boş" mu (ValueObjectEdit EmptyPredicate) — ad (ve fatura adı) + tüm kimlik/iletişim alanları boşsa → placeholder.
    private bool IsEmpty(OrderEditPartyDto? party)
    {
        return party is null
            || (string.IsNullOrWhiteSpace(party.FullName)
                && string.IsNullOrWhiteSpace(BillingName)
                && string.IsNullOrWhiteSpace(party.Email)
                && string.IsNullOrWhiteSpace(party.TcId)
                && string.IsNullOrWhiteSpace(party.TaxOffice)
                && string.IsNullOrWhiteSpace(party.TaxId));
    }

    // ✎ → alıcı popup'ını aç.
    private void OpenPopup()
    {
        _popupVisible = true;
    }

    // İlk boş-olmayan değeri (trim'li) döner; hepsi boşsa null.
    private static string? FirstNonBlank(string? primary, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(primary))
        {
            return primary.Trim();
        }

        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return fallback.Trim();
        }

        return null;
    }
}
