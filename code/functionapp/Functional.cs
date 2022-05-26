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
}

public static class AffModule
{
    public static ValueTask<T> RunAndThrowIfFail<T>(this Aff<T> aff)
    {
        return aff.Run()
                  .Map(fin => fin.ThrowIfFail());
    }
}