using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using DevExpress.Blazor;

namespace Integration.Framework.Blazor.Client.Components.Crud
{
    /// <summary>
    /// Tek bir toolbar aksiyonunun UI-nötr tanımı (ERPPROV3 ToolbarAction kalıbı).
    /// CrudToolbar tüm stock + custom aksiyonları TEK listede toplar, <see cref="SortIndex"/>'e göre
    /// sıralar ve DxToolbar'a tek foreach ile basar → DevExpress'in render/register timing'i devre dışı,
    /// pozisyon yalnız SortIndex'ten gelir (async/geç gelen custom action sona kaymaz).
    ///
    /// Stock SortIndex slotları: Yeni=0, Kaydet=10, Kaydet&Yeni=20, Sil=100, [custom=300 default],
    /// Arama=400, Export=500, Yenile=600, Previous=700, Next=710, Undo=800, Redo=810, Reset=820,
    /// IsActive(Right)=1000. Custom action'lar bu aralıklara (ör. 150, 350) yerleşir.
    /// </summary>
    public sealed class CrudToolbarAction
    {
        /// <summary>Sıralama anahtarı — küçük önce. Stock slotlar arasına custom değer verilir.</summary>
        public int SortIndex { get; init; }

        public string? Text { get; init; }
        /// <summary>Overflow ("⋯") menüsünde gösterilecek metin — kök toolbar'da yalnız ikon istenen item'lar için (Text boş bırakılır).</summary>
        public string? AdaptiveText { get; init; }
        public string? Tooltip { get; init; }
        public string? IconUrl { get; init; }
        public string? IconCssClass { get; init; }

        public bool BeginGroup { get; init; } = true;
        public ToolbarItemAlignment Alignment { get; init; } = ToolbarItemAlignment.Left;
        /// <summary>Primary (dolu) yüzey — ör. Kaydet.</summary>
        public bool Primary { get; init; }

        public bool Visible { get; init; } = true;
        public bool Enabled { get; init; } = true;

        public Func<Task>? OnClick { get; init; }

        /// <summary>Özel içerik (arama kutusu, IsActive switch). Doluysa Text/Icon yerine bu render edilir.
        /// DxToolbarItem.Template ile uyum için tipli (context kullanılmasa da).</summary>
        public RenderFragment<DevExpress.Blazor.IToolbarItemInfo>? Template { get; init; }

        /// <summary>SplitDropDownButton → ana tık + ▾ ile alt menü (ör. Kaydet&Yeni / Kaydet&Kapat).</summary>
        public bool SplitDropDownButton { get; init; }
        /// <summary>Açılır alt item'lar (Export → Excel/PDF, Kaydet&Yeni → Kaydet&Kapat).</summary>
        public IReadOnlyList<CrudToolbarAction>? Items { get; init; }
    }
}
