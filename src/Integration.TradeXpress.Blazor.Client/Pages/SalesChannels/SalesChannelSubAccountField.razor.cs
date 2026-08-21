using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Integration.Framework.Base.Dtos;
using Integration.TradeXpress.Accounts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Integration.TradeXpress.Blazor.Client.Pages.SalesChannels;

public partial class SalesChannelSubAccountField
{
    [Parameter] public Guid? Value { get; set; }

    [Parameter] public EventCallback<Guid?> ValueChanged { get; set; }

    /// <summary>Bağlanan alanın ifadesi — <c>@bind-Value</c> kullanıldığında Blazor OTOMATİK geçirir.
    /// <para>Kirlilik (dirty) takibi buna bağlıdır: <see cref="EditContext"/> hangi alanın değiştiğini ancak
    /// bir <see cref="FieldIdentifier"/> ile bilebilir, o da bu ifadeden üretilir.</para></summary>
    [Parameter] public Expression<Func<Guid?>>? ValueExpression { get; set; }

    /// <summary>Formun düzenleme bağlamı — <c>LookupComboBox</c>'ı içeride çizen bir bileşen olduğumuz için
    /// cascade ile gelir.</summary>
    [CascadingParameter] private EditContext? EditContext { get; set; }

    [Inject] private ISubAccountAppService SubAccountAppService { get; set; } = default!;

    private List<SubAccountListDto> _subAccounts = new();

    protected override async Task OnInitializedAsync()
    {
        // Alt hesap sayısı şirket başına yönetilebilir ölçekte; tamamı bir kez çekilip lookup'a verilir
        // (kanal formu tek seferlik açılır, sayfalı arama karmaşıklığına değmez).
        var page = await SubAccountAppService.GetListAsync(
            new SubAccountListRequestDto { MaxResultCount = ListRequestDto.AllPages });

        _subAccounts = page.Items as List<SubAccountListDto> ?? new List<SubAccountListDto>(page.Items);
    }

    /// <summary>
    /// Seçim değişti: modeli günceller VE <see cref="EditContext"/>'e haber verir.
    ///
    /// <para><b>Bildirim olmadan form KİRLENMİYORDU</b> (2026-08-06 Hakan tespiti): bu bileşen içteki
    /// <c>LookupComboBox</c>'a <c>Value</c>/<c>ValueChanged</c> ile ELLE bağlanıyor, dolayısıyla DevExpress
    /// editörünün kendi <c>ValueExpression</c>'ı yok — <c>EditContext</c> bir alan değiştiğini hiç öğrenmiyor,
    /// <c>IsModified()</c> false kalıyor ve "kaydedilmemiş değişiklik" takibi bu alanı görmüyordu.</para>
    ///
    /// <para>Formdaki diğer alanlar doğrudan <c>@bind-*</c> ile modele bağlı olduğu için bildirimleri
    /// otomatik çıkıyordu; sorun yalnız <c>SalesChannelSubAccountField</c>'ın arkasında kalan bu alandaydı.</para>
    /// </summary>
    private async Task OnChangedAsync(Guid? subAccountId)
    {
        Value = subAccountId;
        await ValueChanged.InvokeAsync(subAccountId);

        if (EditContext is not null && ValueExpression is not null)
        {
            EditContext.NotifyFieldChanged(FieldIdentifier.Create(ValueExpression));
        }
    }
}
