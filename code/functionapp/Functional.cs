using LanguageExt;
using LanguageExt.Common;
using System;
using System.Threading.Tasks;

namespace functionapp;

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
            return Unit.Default;
        });
    }
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
            return Unit.Default;
        });
    }
}