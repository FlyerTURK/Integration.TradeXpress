// Ayarlar panelinin (tema / boyut / dil) tarayıcı tarafı yardımcıları.
// ES modülü olarak lazy import edilir: import('./js/settings.js')

export function getLocal(key) {
    try { return localStorage.getItem(key); }
    catch { return null; }
}

export function setLocal(key, value) {
    try { localStorage.setItem(key, value); }
    catch { /* private mode / quota — yok say */ }
}

export function setBootstrapColorMode(mode) {
    document.documentElement.setAttribute('data-bs-theme', mode);
}

export function setSizeModeAttribute(size) {
    document.documentElement.setAttribute('data-erp-size', size);
}

// Standart cookie yazımı. Çağıran tam değeri verir (ör. "c=tr|uic=tr");
// burada ek bir URL-encode yapılmaz, böylece ASP.NET kültür cookie'si bozulmaz.
export function writeCookie(name, value, days) {
    const maxAge = Math.max(1, Math.floor((days || 365) * 24 * 60 * 60));
    document.cookie = `${name}=${value};path=/;max-age=${maxAge}`;
}

export function reload() {
    location.reload();
}
