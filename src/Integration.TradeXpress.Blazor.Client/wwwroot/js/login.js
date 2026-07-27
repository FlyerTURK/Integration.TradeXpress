// TradeXpress — login page JS helpers.
// erpFetchSignIn, erpFetchFindTenant must run in the browser (not server-side)
// so Set-Cookie headers land in the user's cookie jar, not a server HttpClient container.
window.erpUx = window.erpUx || {};

window.erpFetchSignIn = async function (userName, password, tenant, chosenCulture) {
    try {
        const resp = await fetch('/account/sign-in-cookie', {
            method:      'POST',
            credentials: 'include',
            headers:     { 'Content-Type': 'application/json' },
            body:        JSON.stringify({
                userName:      userName,
                password:      password,
                tenantName:    tenant || null,
                chosenCulture: chosenCulture || null,
            }),
        });
        if (!resp.ok) return { success: false, error: 'HTTP ' + resp.status };
        return await resp.json();
    } catch (e) {
        return { success: false, error: (e && e.message) || 'fetch failed' };
    }
};

window.erpFetchFindTenant = async function (name) {
    try {
        const resp = await fetch('/account/find-tenant?name=' + encodeURIComponent(name), {
            method:      'GET',
            credentials: 'include',
        });
        if (!resp.ok) return { found: false, name: null };
        return await resp.json();
    } catch {
        return { found: false, name: null };
    }
};

(function (ux) {
    function setCookie(name, value, maxAgeDays) {
        const seconds = maxAgeDays * 24 * 60 * 60;
        if (!value) {
            document.cookie = `${name}=; Max-Age=0; Path=/; SameSite=Lax`;
        } else {
            document.cookie = `${name}=${encodeURIComponent(value)}; Max-Age=${seconds}; Path=/; SameSite=Lax`;
        }
    }

    ux.writeCookie = function (name, value, maxAgeDays) {
        setCookie(name, value, maxAgeDays || 365);
    };

    // RAW cookie yazımı — .AspNetCore.Culture için ŞART: CookieRequestCultureProvider decode
    // YAPMAZ, encode'lu değer ("c%3Dtr%7Cuic%3Dtr") dili sessizce varsayılana düşürür.
    ux.writeRawCookie = function (name, value, maxAgeDays) {
        const seconds = (maxAgeDays || 365) * 24 * 60 * 60;
        document.cookie = `${name}=${value}; Max-Age=${seconds}; Path=/; SameSite=Lax`;
    };

    ux.deleteCookie = function (name) {
        document.cookie = `${name}=; Max-Age=0; Path=/; SameSite=Lax`;
    };

    ux.readCookie = function (name) {
        if (!name) return null;
        const target = name + '=';
        const pairs  = document.cookie ? document.cookie.split('; ') : [];
        for (let i = 0; i < pairs.length; i++) {
            if (pairs[i].indexOf(target) === 0)
                return decodeURIComponent(pairs[i].substring(target.length));
        }
        return null;
    };

    ux.focusById = function (id) {
        if (!id) return;
        const el = document.getElementById(id);
        if (el) { try { el.focus(); } catch { /* ignore */ } }
    };

    ux.focusFirstPopupInput = function () {
        setTimeout(() => {
            const popupHeaders = document.querySelectorAll('.dxbs-popup-header');
            popupHeaders.forEach(header => {
                const closeBtns = header.querySelectorAll('.btn-close, .dx-closebutton, button[aria-label="Close"], button.dx-btn-icon');
                closeBtns.forEach(btn => btn.setAttribute('tabindex', '-1'));
            });

            const popupContents = document.querySelectorAll('.dxbs-popup-content');
            if (popupContents.length > 0) {
                const lastContent = popupContents[popupContents.length - 1];
                const allInputs = lastContent.querySelectorAll('input:not([type=hidden]), textarea');
                let targetInput = null;
                
                for(let i = 0; i < allInputs.length; i++) {
                    let el = allInputs[i];
                    if (el.disabled || el.readOnly) continue;
                    
                    let parent = el.closest('.dxbs-editor, .dx-editor, .dx-widget, [class*="readonly"], [class*="disabled"]');
                    if (parent && (parent.classList.contains('dxbs-readonly') || parent.classList.contains('dxbs-disabled') || parent.classList.contains('dx-state-readonly') || parent.classList.contains('dx-state-disabled') || parent.hasAttribute('disabled') || parent.hasAttribute('readonly'))) {
                        continue;
                    }
                    targetInput = el;
                    break;
                }

                if (targetInput) {
                    try {
                        targetInput.focus();
                        if (targetInput.select) targetInput.select();
                    } catch { }
                }
            }
        }, 50);
    };

    ux.focusFirstFormInput = function (formId) {
        setTimeout(() => {
            const form = document.getElementById(formId);
            if (!form) return;
            
            const allInputs = form.querySelectorAll('input:not([type=hidden]), textarea');
            let targetInput = null;
            
            for(let i = 0; i < allInputs.length; i++) {
                let el = allInputs[i];
                if (el.disabled || el.readOnly) continue;
                
                let parent = el.closest('.dxbs-editor, .dx-editor, .dx-widget, [class*="readonly"], [class*="disabled"]');
                if (parent && (parent.classList.contains('dxbs-readonly') || parent.classList.contains('dxbs-disabled') || parent.classList.contains('dx-state-readonly') || parent.classList.contains('dx-state-disabled') || parent.hasAttribute('disabled') || parent.hasAttribute('readonly'))) {
                    continue;
                }
                targetInput = el;
                break;
            }

            if (targetInput) {
                try {
                    targetInput.focus();
                    if (targetInput.select) targetInput.select();
                } catch { }
            }
        }, 100);
    };

    // Video'nun O ANKİ karesini yakala → JPEG base64 + boyut/süre döndür (poster olarak yüklenir).
    // Medya kendi stream endpoint'imizden (same-origin) oynadığından canvas TAINT olmaz; kullanıcı istediği anda durdurup çağırır.
    ux.captureVideoFrame = function (videoId) {
        const video = document.getElementById(videoId);
        // readyState < 2 (HAVE_CURRENT_DATA) → henüz decode edilmiş kare YOK → siyah frame yakalamayı engelle (oynatılması istenir).
        if (!video || !video.videoWidth || !video.videoHeight || video.readyState < 2) {
            return null;
        }
        try {
            // Poster'ı makul boyuta küçült (en uzun kenar 720px) → base64 payload'ı SignalR mesaj sınırında rahat kalır.
            const maxEdge = 720;
            const scale = Math.min(1, maxEdge / Math.max(video.videoWidth, video.videoHeight));
            const canvas = document.createElement('canvas');
            canvas.width = Math.round(video.videoWidth * scale);
            canvas.height = Math.round(video.videoHeight * scale);
            const ctx = canvas.getContext('2d');
            ctx.drawImage(video, 0, 0, canvas.width, canvas.height);
            const dataUrl = canvas.toDataURL('image/jpeg', 0.82);
            return {
                base64: dataUrl.substring(dataUrl.indexOf(',') + 1),
                width: video.videoWidth,      // orijinal video boyutu (metadata)
                height: video.videoHeight,
                duration: isFinite(video.duration) ? video.duration : null,
            };
        } catch {
            return null;   // taint (dış-origin) ya da decode hatası → poster üretilmez
        }
    };

    // Konteyneri görünüme kaydır + içindeki ilk düzenlenebilir input'a odaklan.
    // Kullanım: işlem grid'inde Düzelt → açılan process paneline odak (özellikle mobilde panel ekran dışıysa).
    ux.scrollFocusPanel = function (containerId) {
        setTimeout(() => {
            const panel = document.getElementById(containerId);
            if (!panel) return;
            try { panel.scrollIntoView({ behavior: 'smooth', block: 'start' }); } catch { }
            ux.focusFirstFormInput(containerId);
        }, 150);   // panel render'ının oturması için küçük gecikme (focusFirstFormInput +100ms daha bekler)
    };
})(window.erpUx);
