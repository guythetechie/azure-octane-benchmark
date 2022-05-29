﻿using LanguageExt;
using LanguageExt.Common;
using System;
using System.Threading.Tasks;

using static LanguageExt.Prelude;

namespace common;

public static class EffModule
{
    public static Eff<T> MapFail<T>(this Eff<T> eff, Func<Error, Error> f)
    {
        return eff.BiMap(Prelude.identity, f);
    }

    public static Eff<T> Do<T>(this Eff<T> eff, Action<T> action)
    {
        return eff.Do(t =>
        {
            action(t);
            return unit;
        });
    }

    //    public static Aff<T> Do<T>(this Eff<T> eff, Func<T, ValueTask<Unit>> f)
    //    {
    //#pragma warning disable CA1806 // Do not ignore method results
    //        return eff.Do(t => f(t).ToAff());
    //#pragma warning restore CA1806 // Do not ignore method results
    //    }
}

public static class AffModule
{
    public static ValueTask<T> RunAndThrowIfFail<T>(this Aff<T> aff)
    {
        return aff.Run()
                  .Map(fin => fin.ThrowIfFail());
    }

    public static Aff<T> Do<T>(this Aff<T> aff, Action<T> action)
    {
        return aff.Do(t =>
        {
            action(t);
            return unit;
        });
    }

    public static Aff<T> Do<T>(this Aff<T> aff, Func<T, ValueTask<Unit>> f)
    {
#pragma warning disable CA1806 // Do not ignore method results
        return aff.Do(t => Aff(() => f(t)));
#pragma warning restore CA1806 // Do not ignore method results
    }

    public static Aff<Unit> ToUnit<T>(this Aff<T> aff)
    {
        return aff.Map(_ => unit);
    }

    //    public static Aff<Unit> Iter<T>(this Aff<T> aff, Func<T, ValueTask<Unit>> f)
    //    {
    //        return aff.Iter(t => f(t).ToAff());
    //    }
}