// TradeXpress — login page JS helpers.
// erpFetchSignIn, erpFetchFindTenant must run in the browser (not server-side)
// so Set-Cookie headers land in the user's cookie jar, not a server HttpClient container.
window.erpUx = window.erpUx || {};

window.erpFetchSignIn = async function (userName, password, tenant) {
    try {
        const resp = await fetch('/account/sign-in-cookie', {
            method:      'POST',
            credentials: 'include',
            headers:     { 'Content-Type': 'application/json' },
            body:        JSON.stringify({
                userName:   userName,
                password:   password,
                tenantName: tenant || null,
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
})(window.erpUx);
