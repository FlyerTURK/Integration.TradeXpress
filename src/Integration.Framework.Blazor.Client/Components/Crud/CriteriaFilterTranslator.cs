using System.Globalization;
using DevExpress.Data.Filtering;
using Integration.Framework.Base.Querying;

namespace Integration.Framework.Blazor.Client.Components.Crud;

/// <summary>
/// DevExpress grid'in kolon filtresini (<see cref="CriteriaOperator"/> ağacı) nötr
/// <see cref="FilterField"/> listesine çevirir. Vendor (DevExpress) tipi BURADA biter; sunucuya yalnız
/// vendor-agnostik <see cref="FilterField"/> gider ve server'da <c>ApplyListRequest</c> (whitelist'li,
/// güvenli) bunu IQueryable'a çevirir.
///
/// <para><b>İlk faz kapsamı:</b> AND'li basit kolon filtreleri — <c>[Field] op value</c>
/// (=, ≠, &gt;, ≥, &lt;, ≤) ve string fonksiyonları (Contains/StartsWith/EndsWith). OR grupları,
/// iç içe gruplama, NULL/Between/In gibi karmaşık ifadeler ÇEVRİLMEZ (sessizce atlanır → server'a
/// gönderilmez; yanlış sonuç değil, yalnız o koşul uygulanmaz). Server zaten AND-temelli.</para>
/// </summary>
public static class CriteriaFilterTranslator
{
    public static List<FilterField> Translate(CriteriaOperator? criteria)
    {
        var list = new List<FilterField>();
        if (!ReferenceEquals(criteria, null))
            Collect(criteria, list);
        return list;
    }

    private static void Collect(CriteriaOperator criteria, List<FilterField> list)
    {
        switch (criteria)
        {
            // AND grubu → her operandı topla (server filtreleri zaten AND'ler).
            case GroupOperator g when g.OperatorType == GroupOperatorType.And:
                foreach (var op in g.Operands)
                    if (!ReferenceEquals(op, null)) Collect(op, list);
                break;

            // [prop] op value  /  value op [prop]
            case BinaryOperator b:
                var bf = FromBinary(b);
                if (bf != null) list.Add(bf);
                break;

            // Contains/StartsWith/EndsWith([prop], 'value')
            case FunctionOperator f:
                var ff = FromFunction(f);
                if (ff != null) list.Add(ff);
                break;

            // GroupOperator(Or), UnaryOperator, In/Between/IsNull ... → ilk fazda atla.
        }
    }

    private static FilterField? FromBinary(BinaryOperator b)
    {
        var (prop, val) = ExtractPropAndValue(b.LeftOperand, b.RightOperand);
        if (prop is null) return null;

        ListFilterOperator? op = b.OperatorType switch
        {
            BinaryOperatorType.Equal          => ListFilterOperator.Equals,
            BinaryOperatorType.NotEqual       => ListFilterOperator.NotEquals,
            BinaryOperatorType.Greater        => ListFilterOperator.GreaterThan,
            BinaryOperatorType.GreaterOrEqual => ListFilterOperator.GreaterThanOrEqual,
            BinaryOperatorType.Less           => ListFilterOperator.LessThan,
            BinaryOperatorType.LessOrEqual    => ListFilterOperator.LessThanOrEqual,
            _                                 => null,
        };
        if (op is null) return null;

        return new FilterField { Field = prop, Operator = op.Value, Value = FormatValue(val) };
    }

    private static FilterField? FromFunction(FunctionOperator f)
    {
        ListFilterOperator? op = f.OperatorType switch
        {
            FunctionOperatorType.Contains   => ListFilterOperator.Contains,
            FunctionOperatorType.StartsWith => ListFilterOperator.StartsWith,
            FunctionOperatorType.EndsWith   => ListFilterOperator.EndsWith,
            _                               => null,
        };
        if (op is null || f.Operands.Count < 2) return null;

        var prop = (f.Operands[0] as OperandProperty)?.PropertyName;
        var val  = (f.Operands[1] as OperandValue)?.Value;
        if (string.IsNullOrEmpty(prop)) return null;

        return new FilterField { Field = prop!, Operator = op.Value, Value = FormatValue(val) };
    }

    // [prop] op value veya value op [prop] (her iki sıra) → (property adı, değer)
    private static (string? prop, object? val) ExtractPropAndValue(CriteriaOperator left, CriteriaOperator right)
    {
        if (left is OperandProperty lp && right is OperandValue rv) return (lp.PropertyName, rv.Value);
        if (right is OperandProperty rp && left is OperandValue lv) return (rp.PropertyName, lv.Value);
        return (null, null);
    }

    // Değer string taşınır; server ConvertValue InvariantCulture ile alanın tipine çevirir → burada da Invariant.
    private static string? FormatValue(object? v) => v switch
    {
        null            => null,
        bool b          => b ? "true" : "false",
        DateTime d      => d.ToString("o", CultureInfo.InvariantCulture),
        IFormattable fm => fm.ToString(null, CultureInfo.InvariantCulture),
        _               => v.ToString(),
    };
}
