// Doküman AÇMA yardımcısı — ES modülü olarak lazy import edilir: import('./js/download.js')
// .NET tarafından DotNetStreamReference ile aktarılan içerik tarayıcıya ikili akış olarak geçer
// (base64 şişmesi yok — Blazor Server circuit'inde verimli taşınır).
//
// DAVRANIŞ (2026-07-26 Hakan kararı — uygulama geneli): belge İNDİRİLMEZ/KAYDEDİLMEZ, yeni sekmede AÇILIR.
// Tarayıcının gömülü görüntüleyicisi olan türler (pdf/görsel/metin) doğrudan görünür; olmayanlarında
// (docx/xlsx gibi) kararı tarayıcı verir — bizim tarafımızda indirme tetikleyen kod YOKTUR.
// URL hemen serbest bırakılmaz: sekme yüklenmeden iptal edilirse boş sayfa açılırdı.

export async function openFileFromStream(contentType, contentStreamReference) {
    const arrayBuffer = await contentStreamReference.arrayBuffer();
    const blob = new Blob([arrayBuffer], { type: contentType || 'application/octet-stream' });
    const url = URL.createObjectURL(blob);

    const opened = window.open(url, '_blank');
    setTimeout(() => URL.revokeObjectURL(url), 60000);

    // Açılır pencere engellendiyse çağıran kullanıcıyı uyarabilsin.
    return opened !== null;
}
