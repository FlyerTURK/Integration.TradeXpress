using System;
using Microsoft.AspNetCore.Components;

namespace Integration.Framework.Blazor.Client.Components.Crud;

/// <summary>Form yükleniyor panelinin kod tarafı — gerekçe ve tasarım kararları <c>.razor</c> başındaki
/// açıklamada.</summary>
public partial class FormLoadingPanel
{
    /// <summary>Panelin ortalanacağı alanın en az yüksekliği — YALNIZ boşluk kipinde (üzerine konulacak bir
    /// hedef verilmediğinde). Varsayılan, dönen göstergeyi kırpmadan barındıracak en küçük değerdir.</summary>
    [Parameter] public string MinHeight { get; set; } = "180px";

    /// <summary>
    /// ÜSTÜNE KONULACAK alanın CSS seçicisi (ör. <c>"#form-area-x"</c>). Verilirse panel kendi boşluğunu
    /// açmaz; VAR OLAN içeriğin üzerine gölgeli ve tıklamayı engelleyen bir katman olarak biner.
    ///
    /// <para><b>Neden iki kip:</b> "henüz gösterilecek bir şey yok" ile "düzen çizildi ama verisi gelmedi"
    /// farklı durumlardır. Birincisinde panelin duracağı bir alan yaratmak gerekir; ikincisinde alan zaten
    /// vardır ve asıl istenen, yarı dolu formun kullanılmasını ENGELLEMEKtir — gölge ve tıklama kilidi bu
    /// yüzden yalnız bu kipte açılır.</para>
    /// </summary>
    [Parameter] public string? PositionTargetSelector { get; set; }

    /// <summary>Panel görünür mü. Üstüne-binme kipinde veri gelince <c>false</c>'a çevrilir.</summary>
    [Parameter] public bool Visible { get; set; } = true;

    /// <summary>Panelin konumlandığı kabın DOM id'si — KALICI bir kimlik DEĞİL, yalnız
    /// <c>PositionTarget</c> seçicisi. <c>Guid.NewGuid</c> bu kullanım için açıkça meşrudur
    /// (CLAUDE.md: DOM id / <c>@key</c> gibi kalıcı-id-olmayan değerler).</summary>
    private readonly string _targetId = $"form-loading-{Guid.NewGuid():N}";
}
