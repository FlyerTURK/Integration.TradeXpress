// Tarayıcının IANA timezone kimliğini okur (ör. "Europe/Istanbul").
// Blazor Server'da sunucu, kullanıcının tarayıcı/masaüstü saat dilimini bilmez → bu değer
// ilk render'da (OnAfterRenderAsync) bir kez JS interop ile alınıp UserTimeZoneAccessor'a yazılır.
// Intl API her modern tarayıcıda vardır; yine de çözülemezse null döner (C# tarafı UTC'ye düşer).
export function getTimeZone() {
    try {
        return Intl.DateTimeFormat().resolvedOptions().timeZone || null;
    } catch {
        return null;
    }
}
