using System;

namespace Integration.TradeXpress.Attachments;

/// <summary>Entity-agnostik not — okuma DTO'su (GetForAsync). <see cref="CreationTime"/> = ne zaman eklendiği (audit).</summary>
public class EntityNoteDto
{
    public Guid Id { get; set; }
    public string? Title { get; set; }
    public string Text { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }

    /// <summary>Notun eklenme zamanı (UTC saklanır — UI kullanıcı yereline çevirir).</summary>
    public DateTime CreationTime { get; set; }
}

/// <summary>Entity-agnostik not düzenleme düğümü (drill editör + ReplaceForAsync girdisi).</summary>
public class EntityNoteEditDto
{
    /// <summary>İstemci-tarafı satır anahtarı (@key) — kalıcı değil.</summary>
    public Guid ClientKey { get; set; } = Guid.NewGuid();

    /// <summary>Kaydedilmiş notun kalıcı Id'si — yeni satırda boş.</summary>
    public Guid Id { get; set; }

    public string? Title { get; set; }
    public string? Text { get; set; }
    public int DisplayOrder { get; set; }

    /// <summary>Salt-görüntü: notun eklenme zamanı (mevcut kayıtlarda dolu; yeni satırda null). Save yoksayar.</summary>
    public DateTime? CreationTime { get; set; }
}
