// EditShell başlık şeridi long-press tespiti (mobil) → chrome context menüsü.
// Masaüstü sağ-tık Blazor @oncontextmenu ile gelir; bu modül yalnız dokunmatik/kalem içindir.
// Android long-press NATIVE contextmenu üretir (Blazor handler açar) → burada timer iptal edilir,
// çifte açılma olmaz. iOS contextmenu event'i HİÇ üretmez → menüyü bu timer açar.
const MOVE_TOLERANCE_PX = 10;

export function attachLongPress(element, dotNetRef, delayMs) {
    let timer = null;
    let startX = 0;
    let startY = 0;

    const cancel = () => {
        if (timer) {
            clearTimeout(timer);
            timer = null;
        }
    };

    const onPointerDown = e => {
        if (e.pointerType === 'mouse') return; // fare sağ-tıkla açar; long-press yalnız touch/pen
        startX = e.clientX;
        startY = e.clientY;
        cancel();
        timer = setTimeout(() => {
            timer = null;
            dotNetRef.invokeMethodAsync('OnHeaderLongPress', startX, startY);
        }, delayMs);
    };

    const onPointerMove = e => {
        // Parmak kayması (sürükleme/scroll niyeti) → long-press değil.
        if (timer && (Math.abs(e.clientX - startX) > MOVE_TOLERANCE_PX ||
                      Math.abs(e.clientY - startY) > MOVE_TOLERANCE_PX)) {
            cancel();
        }
    };

    element.addEventListener('pointerdown', onPointerDown);
    element.addEventListener('pointermove', onPointerMove);
    element.addEventListener('pointerup', cancel);
    element.addEventListener('pointercancel', cancel);
    element.addEventListener('contextmenu', cancel); // Android native long-press yolu kazandı → timer sus

    return {
        dispose: () => {
            cancel();
            element.removeEventListener('pointerdown', onPointerDown);
            element.removeEventListener('pointermove', onPointerMove);
            element.removeEventListener('pointerup', cancel);
            element.removeEventListener('pointercancel', cancel);
            element.removeEventListener('contextmenu', cancel);
        }
    };
}
