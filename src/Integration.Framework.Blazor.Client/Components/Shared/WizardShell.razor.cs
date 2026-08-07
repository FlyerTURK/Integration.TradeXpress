using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Microsoft.AspNetCore.Components;

namespace Integration.Framework.Blazor.Client.Components.Shared;

/// <summary>
/// Çok adımlı kurulum kabuğu — <b>kanal/varlık agnostik</b>. Adım listesini, ilerlemeyi, ileri/geri geçişini ve
/// adım-geçiş doğrulamasını yönetir; adımların İÇERİĞİNİ bilmez (<see cref="WizardStep"/> çocukları taşır).
///
/// <para><b>İleri gitmek DOĞRULAMADAN geçer:</b> aktif adımın <see cref="WizardStep.OnBeforeAdvanceAsync"/>'i
/// <c>false</c> dönerse ya da hata fırlatırsa adım DEĞİŞMEZ ve gerekçe kullanıcıya gösterilir. Geri gitmek
/// daima serbesttir (doğrulama çalıştırılmaz) — kullanıcı yazdığını gözden geçirebilmeli.</para>
///
/// <para><b>Adım şeridine tıklama yalnız GERİYE çalışır.</b> İleriye atlamak, atlanan adımın doğrulamasını
/// baypas etmek demekti; kurulum yarım kalır ve bunu ancak sonraki hata gösterirdi.</para>
///
/// <para><b>Eşzamanlılık:</b> geçiş sürerken (<c>_busy</c>) tüm düğmeler kapalıdır — yavaş bir adım
/// (ağ senkronu) çift tıklamayla iki kez koşmaz.</para>
/// </summary>
public partial class WizardShell : CrudComponentBase
{
    private readonly List<WizardStep> _steps = new();
    private int _activeIndex;
    private bool _busy;
    private string? _error;

    /// <summary>Adımlar — doğrudan <see cref="WizardStep"/> çocukları olarak yazılır.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Son adımdaki bitirme düğmesinin metni (boşsa genel "Bitir").</summary>
    [Parameter] public string? FinishText { get; set; }

    /// <summary>Son adımda bitirme düğmesine basıldı. Kabuk yalnız haber verir; ne yapılacağını sahibi bilir
    /// (ör. listeye dön, formu kapat).</summary>
    [Parameter] public EventCallback OnFinished { get; set; }

    /// <summary>Aktif adım değişti — sahibi başlık/yardım metni gibi dış öğeleri hizalayabilsin diye.</summary>
    [Parameter] public EventCallback<int> OnStepChanged { get; set; }

    private WizardStep? ActiveStep
    {
        get { return _activeIndex >= 0 && _activeIndex < _steps.Count ? _steps[_activeIndex] : null; }
    }

    private bool IsLastStep
    {
        get { return _steps.Count == 0 || _activeIndex >= _steps.Count - 1; }
    }

    // ── Adım kaydı (çocuklar kendilerini kaydeder) ──────────────────────────────────────────────────

    /// <summary>Çocuk adım kendini kaydeder (ilk render'da). Sıra, işaretlemedeki yazım sırasıdır.</summary>
    internal void RegisterStep(WizardStep step)
    {
        if (_steps.Contains(step))
        {
            return;
        }

        _steps.Add(step);
        StateHasChanged();
    }

    /// <summary>Adım tamamen kaldırıldıysa (koşullu render) listeden düşer; aktif indeks sınırda tutulur.</summary>
    /// <summary>Adımın parametresi (ör. <c>CanAdvance</c>) değişti → kabuk yeniden çizilsin.
    /// Düğmeleri kabuk çizdiği için, adımın kendi render'ı tek başına yetmez.</summary>
    internal void NotifyStepStateChanged()
    {
        StateHasChanged();
    }

    internal void RemoveStep(WizardStep step)
    {
        if (!_steps.Remove(step))
        {
            return;
        }

        if (_activeIndex >= _steps.Count)
        {
            _activeIndex = Math.Max(0, _steps.Count - 1);
        }

        StateHasChanged();
    }

    /// <summary>Bu adım şu an görünür mü — <see cref="WizardStep"/> içeriğini yalnız aktifken çizer.</summary>
    internal bool IsActive(WizardStep step)
    {
        return ActiveStep == step;
    }

    // ── Geçişler ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>İleri: önce aktif adımın doğrulaması/işi koşar. <c>false</c> döner ya da hata fırlatırsa adım
    /// DEĞİŞMEZ — yarım kurulumla ilerlemek, hatayı sonraki adıma taşımaktır.</summary>
    private async Task NextAsync()
    {
        await AdvanceAsync(runStepWork: true);
    }

    /// <summary>Atla: adımın işini KOŞMADAN ilerler. Yalnız <see cref="WizardStep.CanSkip"/> işaretli adımda
    /// düğme çıkar (ör. gider ayarı — dokunulmazsa kategori oranı kullanılır).</summary>
    private async Task SkipAsync()
    {
        await AdvanceAsync(runStepWork: false);
    }

    private async Task AdvanceAsync(bool runStepWork)
    {
        if (_busy || IsLastStep)
        {
            return;
        }

        _busy = true;
        _error = null;
        try
        {
            if (runStepWork && ActiveStep?.OnBeforeAdvanceAsync.HasDelegate == true)
            {
                var proceed = await ActiveStep.RunBeforeAdvanceAsync();
                if (!proceed)
                {
                    return;   // adım kendi gerekçesini kendi gösterir (kabuk metnini bilmez)
                }
            }

            _activeIndex++;
            await NotifyStepChangedAsync();
        }
        catch (Exception ex)
        {
            _error = FriendlyMessage(ex);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Geri: doğrulama KOŞULMAZ. Kullanıcı girdiğini gözden geçirmek için serbestçe geri gidebilmeli;
    /// geri giderken doğrulamak, düzeltmek isteyeni düzeltmeden önce engellemek olurdu.</summary>
    private async Task BackAsync()
    {
        if (_busy || _activeIndex == 0)
        {
            return;
        }

        _error = null;
        _activeIndex--;
        await NotifyStepChangedAsync();
    }

    /// <summary>Şeritten tıklama — YALNIZ geriye. İleriye atlamak aradaki adımların doğrulamasını baypas ederdi.</summary>
    private async Task GoBackToAsync(int index)
    {
        if (_busy || index >= _activeIndex || index < 0)
        {
            return;
        }

        _error = null;
        _activeIndex = index;
        await NotifyStepChangedAsync();
    }

    private async Task FinishAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        _error = null;
        try
        {
            // Son adımın da işi olabilir (ör. özet ekranı öncesi son kayıt) — ileri geçişle AYNI sözleşme.
            if (ActiveStep?.OnBeforeAdvanceAsync.HasDelegate == true && !await ActiveStep.RunBeforeAdvanceAsync())
            {
                return;
            }

            await OnFinished.InvokeAsync();
        }
        catch (Exception ex)
        {
            _error = FriendlyMessage(ex);
        }
        finally
        {
            _busy = false;
        }
    }

    private Task NotifyStepChangedAsync()
    {
        return OnStepChanged.HasDelegate ? OnStepChanged.InvokeAsync(_activeIndex) : Task.CompletedTask;
    }

    /// <summary>Adım işinden gelen hatayı kullanıcı diline çevirir. Kabuk uygulamanın hata kataloğunu bilmez →
    /// çeviri sahibinin sorumluluğunda; burada yalnız mesaj gösterilir (yutulmaz).</summary>
    private static string FriendlyMessage(Exception ex)
    {
        return ex.Message;
    }

    // ── Şerit görünümü ──────────────────────────────────────────────────────────────────────────────

    private WizardStepState StateOf(int index)
    {
        if (index < _activeIndex)
        {
            return WizardStepState.Done;
        }

        return index == _activeIndex ? WizardStepState.Current : WizardStepState.Upcoming;
    }

    private static string BadgeStyle(WizardStepState state)
    {
        var baseStyle = "display:flex; align-items:center; gap:6px; padding:4px 10px; border-radius:14px; font-size:0.85rem;";
        return state switch
        {
            WizardStepState.Current => baseStyle + " background:#0d6efd; color:#fff; cursor:default;",
            WizardStepState.Done => baseStyle + " background:#e7f1ff; color:#0d6efd; cursor:pointer;",
            _ => baseStyle + " background:#f1f3f5; color:#868e96; cursor:default;",
        };
    }

    private static string NumberStyle(WizardStepState state)
    {
        var baseStyle = "display:inline-flex; align-items:center; justify-content:center; width:18px; height:18px; border-radius:50%; font-size:0.75rem; font-weight:600;";
        return state switch
        {
            WizardStepState.Current => baseStyle + " background:#fff; color:#0d6efd;",
            WizardStepState.Done => baseStyle + " background:#0d6efd; color:#fff;",
            _ => baseStyle + " background:#dee2e6; color:#868e96;",
        };
    }

    private enum WizardStepState
    {
        Done,
        Current,
        Upcoming,
    }
}
