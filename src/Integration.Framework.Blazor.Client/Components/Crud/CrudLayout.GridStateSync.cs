using Integration.Framework.Base.Dtos;

namespace Integration.Framework.Blazor.Client.Components.Crud;

// Grid fetch → merkezi StateService senkronu — okunabilirlik için ayrı partial dosyada.
public partial class CrudLayout<TGetDto, TListDto, TKey>
{
    // Grid her fetch ettiğinde (Fetched) merkezi StateService'i tazele: yüklü sayfa + sayfa-aşırı durumu.
    // (Grid fetch'i CrudLayout'u re-render etmediğinden eski OnAfterRender senkronu güvenilmezdi.)
    private void SyncStateFromGrid()
    {
        if (_gridSource is not { } ds)
        {
            return;
        }
        var req = ds.LastRequest;
        InvokeAsync(async () =>
        {
            PushLoadedPageToState(ds);
            ApplyRequestToState(req);
            await ReconcileSelectionAfterFetchAsync(ds);
            StateHasChanged();
        });
    }

    /// <summary>Yüklü sayfa kayıtlarını + toplam sayıyı merkezi StateService'e yazar.</summary>
    private void PushLoadedPageToState(GridListDataSource<TListDto> ds)
    {
        StateService.ListDataSource = new List<TListDto>(ds.CurrentItems);
        StateService.TotalCount     = ds.TotalCount;
    }

    /// <summary>Fetch isteğinin parametrelerini (skip/size/sort/filtre) state'e yansıtır ve
    /// controlled PageIndex'i fetch edilen GERÇEK sayfaya eşitler.</summary>
    private void ApplyRequestToState(ListRequestDto? req)
    {
        if (req == null)
        {
            return;
        }

        StateService.PageSkip       = req.SkipCount;
        StateService.PageSize       = req.MaxResultCount;
        StateService.Sorts          = req.Sorts;
        StateService.Filter         = req.Filter;
        StateService.IsActiveFilter = req.IsActive;

        // @bind-PageIndex YARIŞ DÜZELTMESİ: bu fetch'in StateHasChanged'i, DxGrid'in
        // PageIndexChanged writeback'inden (_gridPageIndex=hedef) ÖNCE koşarsa, re-render eski
        // _gridPageIndex'i (genelde 0) grid'e geri basıp sayfayı 1'e snap'liyordu ("bazen 2.→1."").
        // Fetch edilen GERÇEK sayfayı (skip÷size) controlled değere yaz → hep gerçeğe eşit, snap yok.
        if (req.MaxResultCount > 0)
        {
            _gridPageIndex = req.SkipCount / req.MaxResultCount;
        }
    }

    /// <summary>Düz liste sayfası: fetch sonrası mevcut seçili kayıt yeni yüklü sayfada hâlâ varsa korunur;
    /// yoksa İLK kayıt SEÇİLİR (FocusDataItemAsync artık odak değil seçim yapar), sayfa boşsa seçim
    /// temizlenir → sayfa değişince eski sayfanın kaydı Sil'e açık kalmaz. Split kendi grid'ini yönetir.
    /// ÇOKLU SEÇİM (checkbox, &gt;1) sürerken fetch sonrası ilk-satır seçimi EZMESİN.</summary>
    private async Task ReconcileSelectionAfterFetchAsync(GridListDataSource<TListDto> ds)
    {
        if (SplitHost != null || (StateService.SelectedDataItems?.Count ?? 0) > 1)
        {
            return;
        }

        var current = StateService.SelectedItem;
        var stillThere = false;
        if (current != null)
        {
            foreach (var it in ds.CurrentItems)
            {
                if (Equals(it.Id, current.Id))
                {
                    stillThere = true;
                    break;
                }
            }
        }
        if (stillThere)
        {
            return;
        }

        if (ds.CurrentItems.Count > 0)
        {
            await ((ISplitGridActions)this).FocusDataItemAsync(ds.CurrentItems[0].Id);
        }
        else
        {
            StateService.SetDataRowSelected(null!);
        }
    }
}
