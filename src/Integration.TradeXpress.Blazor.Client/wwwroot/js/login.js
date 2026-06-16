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
})(window.erpUx);
