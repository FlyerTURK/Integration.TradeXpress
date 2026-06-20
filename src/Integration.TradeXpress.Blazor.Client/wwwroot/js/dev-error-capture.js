// Geliştirici hata yakalama: console.error/warn + window 'error' + 'unhandledrejection' olaylarını
// .NET tarafına (DeveloperErrorPanel.Report) iletir. Blazor, ele alınmayan .NET exception'larını da
// console.error'a yazdığı için bu kanca onları da yakalar. Kurulum yalnız bir kez yapılır.

let _ref = null;
let _installed = false;

export function init(dotNetRef) {
    _ref = dotNetRef;
    if (_installed) return;
    _installed = true;

    // Zararsız tarayıcı/bileşen gürültüsü — panele DÜŞMEMELİ. "ResizeObserver loop ..." Chrome'un
    // benign uyarısıdır (DevExpress'in resize-gözlemli bileşenleri bir frame'e sığmayınca fırlar;
    // gerçek hata değil). Mesajı içeren her kayıt (console/js/rejection) sessizce yutulur.
    const IGNORED = [
        'ResizeObserver loop',
    ];

    const report = (level, source, message, stack) => {
        const text = message || '';
        if (IGNORED.some(p => text.indexOf(p) !== -1)) return;   // gürültü → atla
        // Panel (ref) yoksa ya da çağrı patlarsa sessizce geç — asla console.error'a düşme (döngü olur).
        try {
            if (_ref) _ref.invokeMethodAsync('Report', level, source, message || '', stack || null);
        } catch (e) { /* yut */ }
    };

    const fmt = (args) => args.map(a => {
        if (a instanceof Error) return a.message;
        if (a && typeof a === 'object') { try { return JSON.stringify(a); } catch (e) { return String(a); } }
        return String(a);
    }).join(' ');

    const stackOf = (args) => {
        const err = args.find(a => a instanceof Error);
        return err ? err.stack : null;
    };

    const origError = console.error.bind(console);
    const origWarn = console.warn.bind(console);

    console.error = (...args) => { origError(...args); report('error', 'console', fmt(args), stackOf(args)); };
    console.warn = (...args) => { origWarn(...args); report('warn', 'console', fmt(args), null); };

    window.addEventListener('error', (e) => {
        const msg = e.message || (e.error && e.error.message) || 'Script error';
        const stack = (e.error && e.error.stack) || (e.filename ? `${e.filename}:${e.lineno}:${e.colno}` : null);
        report('error', 'js', msg, stack);
    });

    window.addEventListener('unhandledrejection', (e) => {
        const r = e.reason;
        const msg = (r && (r.message || (r.toString && r.toString()))) || 'Unhandled promise rejection';
        const stack = (r && r.stack) || null;
        report('rejection', 'js', msg, stack);
    });
}

// Panel dispose olunca ref'i bırak (eski referansa çağrı yapılmasın).
export function detach() {
    _ref = null;
}

// Paneldeki "Kopyala" için: metni clipboard'a yazar.
export function copyText(text) {
    try {
        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(text);
            return true;
        }
    } catch (e) { /* yut */ }
    return false;
}
