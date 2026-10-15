using Xunit;

namespace StellarPerformance.Tests;

internal static class OptionalModTests
{
#if OPTIONAL_MOD_TESTS
    internal static readonly bool Enabled = true;
#else
    internal static readonly bool Enabled = false;
#endif
}

public sealed class OptionalModFactAttribute : FactAttribute
{
    public OptionalModFactAttribute()
    {
        if (!OptionalModTests.Enabled)
            Skip = "Optional adapter coverage: run with -p:IncludeOptionalModTests=true and the documented mod versions installed.";
    }
}
