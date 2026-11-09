// Unity's .NET profile lacks this type, which C# 9 records and init setters need.
#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
#endif
