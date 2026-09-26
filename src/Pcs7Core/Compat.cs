using System;

namespace Pcs7Core
{
    /// <summary>Small helpers so the same sources compile on .NET Framework 4.8 and .NET 8.</summary>
    public static class Compat
    {
        public static bool ContainsCI(this string? s, string? value) =>
            s is not null && value is not null && s.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;

        public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
    }
}

#if NETFRAMEWORK
namespace System.Runtime.CompilerServices
{
    // Enables records and init-only setters on .NET Framework.
    internal static class IsExternalInit { }
}
#endif
