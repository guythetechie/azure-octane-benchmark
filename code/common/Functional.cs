using LanguageExt;
using System;

namespace common;

public static class OptionModule
{
    public static T IfNoneThrow<T>(this Option<T> option, string errorMessage)
    {
        return option.IfNone(() => throw new InvalidOperationException(errorMessage));
    }
}