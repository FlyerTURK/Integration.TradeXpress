using Volo.Abp;

namespace Integration.Framework;

/// <summary>Zorunlu alan boş bırakıldı. Lokalize mesaj: <c>{Property}</c>.</summary>
public class RequiredPropertyException : BusinessException
{
    public RequiredPropertyException(string propertyName)
        : base(FrameworkErrorCodes.PropertyRequired)
    {
        WithData("Property", propertyName);
    }
}

/// <summary>Alan minimum uzunluktan kısa. Lokalize: <c>{Property}</c>, <c>{Min}</c>.</summary>
public class TooShortPropertyException : BusinessException
{
    public TooShortPropertyException(string propertyName, int minLength)
        : base(FrameworkErrorCodes.PropertyTooShort)
    {
        WithData("Property", propertyName);
        WithData("Min", minLength);
    }
}

/// <summary>Alan maksimum uzunluğu aştı. Lokalize: <c>{Property}</c>, <c>{Max}</c>.</summary>
public class TooLongPropertyException : BusinessException
{
    public TooLongPropertyException(string propertyName, int maxLength)
        : base(FrameworkErrorCodes.PropertyTooLong)
    {
        WithData("Property", propertyName);
        WithData("Max", maxLength);
    }
}

/// <summary>Sayısal alan izinli aralık dışında. Lokalize: <c>{Property}</c>, <c>{Min}</c>, <c>{Max}</c>.</summary>
public class OutOfRangePropertyException : BusinessException
{
    public OutOfRangePropertyException(string propertyName, int min, int max)
        : base(FrameworkErrorCodes.PropertyOutOfRange)
    {
        WithData("Property", propertyName);
        WithData("Min", min);
        WithData("Max", max);
    }
}
