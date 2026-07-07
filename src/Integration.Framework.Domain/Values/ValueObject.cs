using System;
using System.Collections.Generic;
using System.Linq;

namespace Integration.Framework.Values;

/// <summary>
/// Framework value object tabanı — <b>değer eşitliğini</b> (<see cref="Equals(object?)"/> / <c>==</c> / <c>!=</c> /
/// <see cref="GetHashCode"/>) <see cref="GetAtomicValues"/> üzerinden BİR KEZ sağlar. ABP'nin <c>ValueObject</c>'i
/// bu sürümde Equals/==/hashcode vermiyor (yalnız ValueEquals) → her VO'da tekrar yazmamak için (kullanıcı kararı
/// 2026-07-07). Türeyen value object (Address, Money, ölçü, …) yalnız <see cref="GetAtomicValues"/>'ı doldurur;
/// değer-semantiğini bedava alır. Immutable olmalı; EF <c>OwnsOne</c>/<c>OwnsMany</c> ile gömülür.
/// </summary>
public abstract class ValueObject
{
    /// <summary>Eşitliği ve hashcode'u belirleyen atomik değerler (alanlar), sıralı. null öğeler serbest.</summary>
    protected abstract IEnumerable<object?> GetAtomicValues();

    /// <summary>Tip + tüm atomik değerleri sırayla karşılaştırır (ABP ValueObject semantiği).</summary>
    public bool ValueEquals(object? obj)
    {
        if (obj is null || obj.GetType() != GetType())
        {
            return false;
        }

        return GetAtomicValues().SequenceEqual(((ValueObject)obj).GetAtomicValues());
    }

    public override bool Equals(object? obj)
    {
        return ValueEquals(obj);
    }

    public override int GetHashCode()
    {
        var hash = default(HashCode);
        foreach (var value in GetAtomicValues())
        {
            hash.Add(value);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(ValueObject? left, ValueObject? right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(ValueObject? left, ValueObject? right)
    {
        return !Equals(left, right);
    }
}
