using System;
using System.Collections.Generic;
using System.Linq;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Attachments;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Components.Shared;

/// <summary>Entity-agnostik not paneli (reusable DrillList) — herhangi bir entity kaydının sade metin notları.
/// Sahip form bir DxTabPage içine koyar. Graf save sahip AppService'te (IEntityNoteAppService.ReplaceForAsync).</summary>
public partial class EntityNotesPanel
{
    [Parameter, EditorRequired] public List<EntityNoteEditDto> Notes { get; set; } = default!;

    // Drill değişimini forma bildir (dirty/Save) — EntityEditForm EditChanged cascade'i.
    [CascadingParameter(Name = "EditChanged")] private Action? EditChanged { get; set; }

    private DrillList<EntityNoteEditDto>? _drill;

    // Yeni not eklenince Sıra No otomatik artar (mevcutların max'ı + 1; boşsa 1).
    private int NextOrder()
    {
        return Notes.Select(x => x.DisplayOrder).DefaultIfEmpty(0).Max() + 1;
    }

    // Başlık boşsa metnin önizlemesini birincil değer olarak kullan (drill başlığı + link kolonu).
    private static string? TitleOrTextOf(EntityNoteEditDto note)
    {
        return string.IsNullOrWhiteSpace(note.Title) ? Preview(note.Text) : note.Title;
    }

    // Metin önizlemesi (tek satır, ilk 60 karakter) — grid satırı taşmasın.
    private static string? Preview(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var singleLine = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return singleLine.Length <= 60 ? singleLine : singleLine.Substring(0, 60) + "...";
    }

    // Kaydetme engeli: metni boş not kabul edilmez (sunucu ReplaceFor da eler — savunma).
    private string? NoteSaveGuard(EntityNoteEditDto candidate)
    {
        return string.IsNullOrWhiteSpace(candidate.Text) ? L["TradeXpress:Note:TextRequired"].Value : null;
    }
}
