using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;

namespace Integration.Framework.Blazor.Client.Components.Crud;

/// <summary>
/// <b>Buffered giriş panellerinin</b> jenerik tabanı (süreç-paneli deseni; app-agnostik — DrillList gibi).
/// Yaşam döngüsü: toolbar'dan <see cref="OpenDraft"/> ile yeni DRAFT açılır ya da <see cref="BeginEdit"/> ile
/// mevcut öğenin KOPYASI draft olur (buffered — orijinale ANLIK yazılmaz); <see cref="SaveDraftAsync"/> draft'ı
/// uygular (yeni → listeye ekler, düzenleme → orijinali günceller), <see cref="OnChanged"/> tetikler ve
/// <b>panel açık kalır</b>: <see cref="CreateNextDraft"/> ile aynı tipte yeni draft hazırlanır (seri giriş);
/// <see cref="CloseDraft"/> draft'ı atar (iptal). Chrome (başlık şeridi + Kaydet/Geri) = <c>EntryPanelShell</c>.
///
/// <para>Türev yalnız kendi alan içeriğini + öğe kopyalama/uygulama/sıradaki-draft kurallarını verir
/// (<see cref="CloneItem"/> / <see cref="ApplyDraft"/> / <see cref="CreateNextDraft"/>). İlk türev:
/// TradeXpress <c>ProductRecipePanel</c>; 3b/3c satır panelleri ve diğer giriş panelleri aynı tabanı devralır.</para>
/// </summary>
public abstract class EntryPanelBase<TItem> : CrudComponentBase
    where TItem : class
{
    /// <summary>Değişiklik bildirimi (parent form dirty/Save) — YALNIZ draft uygulanınca/öğe silinince.</summary>
    [Parameter] public EventCallback OnChanged { get; set; }

    /// <summary>Panelin DRAFT'ı (kopya öğe) — null = panel kapalı (toolbar görünür).</summary>
    protected TItem? Draft { get; private set; }

    /// <summary>Düzenlenen ORİJİNAL öğe; null = yeni öğe girişi.</summary>
    protected TItem? EditingItem { get; private set; }

    /// <summary>Panel açık mı (draft var mı) — markup dallanması için.</summary>
    protected bool IsPanelOpen
    {
        get { return Draft != null; }
    }

    /// <summary>Draft'ın uygulanacağı hedef koleksiyon (türev kendi listesini verir).</summary>
    protected abstract IList<TItem> ItemsSource { get; }

    /// <summary>Öğenin buffered KOPYASI (düzenleme draft'ı) — alan-alan kopya, referans paylaşımı YOK.</summary>
    protected abstract TItem CloneItem(TItem source);

    /// <summary>Draft'ı orijinal öğeye uygular (kimlik alanları hedefte kalır).</summary>
    protected abstract void ApplyDraft(TItem draft, TItem target);

    /// <summary>Kaydet sonrası SIRADAKİ draft (seri giriş): uçucu alanlar sıfır, sınıflandırma/seçimler korunur.</summary>
    protected abstract TItem CreateNextDraft(TItem saved);

    /// <summary>Yeni öğe draft'ı ile paneli açar (toolbar butonu çağırır).</summary>
    protected void OpenDraft(TItem draft)
    {
        EditingItem = null;
        Draft = draft;
    }

    /// <summary>Mevcut öğeyi düzenlemeye alır — draft = KOPYA (orijinal Kaydet'e dek değişmez).</summary>
    protected void BeginEdit(TItem item)
    {
        EditingItem = item;
        Draft = CloneItem(item);
    }

    /// <summary>Draft'ı ATAR (iptal) — panel kapanır, toolbar döner; düzenlenen orijinal DEĞİŞMEMİŞ kalır.</summary>
    protected void CloseDraft()
    {
        Draft = null;
        EditingItem = null;
    }

    /// <summary>Draft'ı uygular (yeni → ekle, düzenleme → güncelle) + <see cref="OnChanged"/> + sıradaki draft.</summary>
    protected async Task SaveDraftAsync()
    {
        if (Draft is not { } draft)
        {
            return;
        }

        if (EditingItem is { } target)
        {
            ApplyDraft(draft, target);
        }
        else
        {
            ItemsSource.Add(draft);
        }

        await OnChanged.InvokeAsync();

        EditingItem = null;
        Draft = CreateNextDraft(draft);
    }

    /// <summary>Öğe silindiğinde çağrılır: silinen öğe düzenleniyorduysa draft da atılır + değişiklik bildirilir.
    /// (Silme kuralının kendisi — soft/hard — türevdedir.)</summary>
    protected async Task NotifyItemRemovedAsync(TItem item)
    {
        if (ReferenceEquals(EditingItem, item))
        {
            CloseDraft();
        }

        await OnChanged.InvokeAsync();
    }
}
