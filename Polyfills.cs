// Polyfills for netstandard2.0 compatibility with C# 9+ features

#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Reserved for use by the compiler for record types.
    /// </summary>
    internal static class IsExternalInit { }
}
#endif
