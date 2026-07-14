// Doküman indirme yardımcısı — ES modülü olarak lazy import edilir: import('./js/download.js')
// .NET tarafından DotNetStreamReference ile aktarılan içeriği tarayıcıda dosya olarak indirir
// (base64 şişmesi yok — Blazor Server circuit'inde ikili akış verimli taşınır).

export async function downloadFileFromStream(fileName, contentType, contentStreamReference) {
    const arrayBuffer = await contentStreamReference.arrayBuffer();
    const blob = new Blob([arrayBuffer], { type: contentType || 'application/octet-stream' });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName || 'download';
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(url);
}
