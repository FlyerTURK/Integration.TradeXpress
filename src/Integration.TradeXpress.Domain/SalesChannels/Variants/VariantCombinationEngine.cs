namespace Integration.TradeXpress.SalesChannels.Variants;

/// <summary>
/// Varyant kombinasyon matematiğinin TEK kaynağı — SAF statik (DB'siz, repository'siz, DI'sız).
/// ERP <c>ProductVariantSynchronizer</c> ve kanal (N11/Trendyol) reconcile akışları kartezyen/anahtar
/// üretimini buradan alır; orkestrasyon (yükleme, silme/ekleme politikası, guard'lar) ÇAĞIRANDA kalır.
/// <para><b>Semantik sözleşme (mevcut davranışın birebir generic'i — DEĞİŞTİRME):</b></para>
/// <list type="bullet">
/// <item>Boş eksen listesi → TEK BOŞ kombinasyon (çarpımın birim elemanı). "0 eksen = kombinasyon yok"
/// yorumu çağıranın guard'ıdır (ERP synchronizer 0-attribute dalını kartezyene hiç girmeden ele alır).</item>
/// <item>Değersiz eksen → BOŞ sonuç (matematiksel doğru: çarpanlardan biri 0). Bunu "mevcut seti koru"
/// diye yorumlamak da çağıranın kararıdır (ERP synchronizer değersiz-ekseni kendi guard'ıyla atlar).</item>
/// <item>Kombinasyon sırası deterministik: eksen giriş sırası × değer giriş sırası (soldan sağa çarpım).</item>
/// </list>
/// </summary>
public static class VariantCombinationEngine
{
    /// <summary>Eksen×değer kartezyeni — her eksenden BİR değer; kombinasyon = (eksen, değer) çiftleri,
    /// eksen giriş sırasıyla. ERP synchronizer'ın (attribute, value) entity çiftleri ve kanal reconcile'ın
    /// (AxisId, ValueId) çiftleri aynı motoru kullanır.</summary>
    public static List<List<(TAxis Axis, TValue Value)>> BuildCartesian<TAxis, TValue>(
        IReadOnlyList<(TAxis Axis, IReadOnlyList<TValue> Values)> axes)
    {
        var result = new List<List<(TAxis, TValue)>> { new() };
        foreach (var (axis, axisValues) in axes)
        {
            result = result
                .SelectMany(prefix => axisValues.Select(value =>
                {
                    var next = new List<(TAxis, TValue)>(prefix) { (axis, value) };
                    return next;
                }))
                .ToList();
        }

        return result;
    }

    /// <summary>Değer-listesi kartezyeni — eksen nesnesi taşımayan tüketiciler için (ör. persistsiz üretim
    /// önizlemesinin DTO eksenleri). Semantik çiftli overload ile AYNI (boş liste → tek boş kombinasyon;
    /// değersiz eksen → boş sonuç).</summary>
    public static List<List<TValue>> BuildCartesian<TValue>(IReadOnlyList<IReadOnlyList<TValue>> axes)
    {
        var result = new List<List<TValue>> { new() };
        foreach (var axisValues in axes)
        {
            result = result
                .SelectMany(prefix => axisValues.Select(value =>
                {
                    var next = new List<TValue>(prefix) { value };
                    return next;
                }))
                .ToList();
        }

        return result;
    }

    /// <summary>Kombinasyon imzası — SIRALI (artan Guid) "|" join. Sıra bağımsız deterministik anahtar:
    /// aynı id kümesi hangi sırayla gelirse gelsin aynı imzayı üretir. Format tüketici-yereldir ve opak
    /// kullanılır (sistemler arası geçmez); MEVCUT ERP formatının birebiri — DEĞİŞTİRME (testler snapshot'ladı).</summary>
    public static string BuildKey(IEnumerable<Guid> ids)
    {
        return string.Join("|", ids.OrderBy(id => id));
    }
}
