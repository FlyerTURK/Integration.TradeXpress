using System;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Microsoft.AspNetCore.Components;

namespace Integration.Framework.Blazor.Client.Components.Shared;

/// <summary>
/// <see cref="WizardShell"/>'in tek adımı. Kendini kabuğa kaydeder, başlığını şeride verir ve içeriğini
/// YALNIZ aktifken çizer.
///
/// <para><b>Neden pasif adım hiç render edilmiyor:</b> görünmeyen bir form alanı doğrulamayı tetikleyebilir
/// (kullanıcı sebebini göremediği bir hata alır) ve ağ çağrısı yapan bir alt bileşen sırası gelmeden koşabilir.
/// Adım gövdesini koşullu çizmek ikisini de kapatır.</para>
///
/// <para><b><see cref="OnBeforeAdvanceAsync"/> sözleşmesi:</b> "İleri"ye basıldığında koşar ve
/// <c>false</c> dönerse adım DEĞİŞMEZ. Gerekçeyi ADIM gösterir (kabuk uygulamanın hata dilini bilmez).
/// Tanımlı değilse geçiş koşulsuzdur.</para>
/// </summary>
public partial class WizardStep : CrudComponentBase, IDisposable
{
    [CascadingParameter] private WizardShell? Shell { get; set; }

    /// <summary>Adım şeridinde görünen kısa başlık.</summary>
    [Parameter, EditorRequired] public string Title { get; set; } = string.Empty;

    /// <summary>Adımın üstünde gösterilen açıklama (opsiyonel) — ne yapılacağını bir cümleyle anlatır.</summary>
    [Parameter] public string? Description { get; set; }

    /// <summary>Bu adım atlanabilir mi — işaretliyse ayağa "Atla" düğmesi çıkar ve
    /// <see cref="OnBeforeAdvanceAsync"/> KOŞULMADAN ilerlenir.</summary>
    [Parameter] public bool CanSkip { get; set; }

    /// <summary>"İleri" basıldığında koşan iş/doğrulama. <c>false</c> → adım değişmez.</summary>
    [Parameter] public EventCallback<WizardStepAdvanceContext> OnBeforeAdvanceAsync { get; set; }

    [Parameter] public RenderFragment? ChildContent { get; set; }

    private bool IsActive
    {
        get { return Shell?.IsActive(this) == true; }
    }

    protected override void OnInitialized()
    {
        Shell?.RegisterStep(this);
    }

    /// <summary>Kabuk çağırır: adımın işini koşar ve "ilerlenebilir mi"yi döner. Bağlam nesnesi, adımın
    /// <c>Cancel()</c> diyebilmesi içindir — <c>EventCallback</c> değer döndüremediğinden bayrak buradan taşınır.</summary>
    internal async Task<bool> RunBeforeAdvanceAsync()
    {
        var context = new WizardStepAdvanceContext();
        await OnBeforeAdvanceAsync.InvokeAsync(context);
        return !context.Cancelled;
    }

    public void Dispose()
    {
        Shell?.RemoveStep(this);
    }
}

/// <summary>Adım geçiş bağlamı — adımın "ilerleme" diyebilmesi için. <see cref="EventCallback"/> değer
/// döndüremediğinden karar bu nesne üzerinden taşınır (Blazor'da yerleşik desen).</summary>
public sealed class WizardStepAdvanceContext
{
    /// <summary>Adım ilerlemeyi ENGELLEDİ mi.</summary>
    public bool Cancelled { get; private set; }

    /// <summary>İlerlemeyi engelle — adım gerekçeyi kendi yüzeyinde göstermiş olmalıdır.</summary>
    public void Cancel()
    {
        Cancelled = true;
    }
}
