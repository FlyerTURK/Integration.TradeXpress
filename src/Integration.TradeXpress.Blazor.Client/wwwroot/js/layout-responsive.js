// Layout responsive yardımcıları — mobil/masaüstü tespiti + breakpoint geçiş bildirimi.
// MainLayout, sol menü drawer'ını mobilde Overlap+kapalı, masaüstünde Shrink+açık yapmak için kullanır.
const MOBILE_QUERY = '(max-width: 767.98px)';

export function isMobile() {
    return window.matchMedia(MOBILE_QUERY).matches;
}

// matchMedia 'change' yalnız breakpoint geçilince tetiklenir (resize spam'i yok).
export function registerBreakpoint(dotNetRef) {
    const mql = window.matchMedia(MOBILE_QUERY);
    const handler = e => dotNetRef.invokeMethodAsync('OnViewportChanged', e.matches);
    mql.addEventListener('change', handler);
    return {
        dispose: () => mql.removeEventListener('change', handler)
    };
}
