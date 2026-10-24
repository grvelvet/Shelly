using Xunit;

namespace Shelly.Core.Tests;

/// <summary>A test that needs PowerShell. Without it the test is reported as skipped (with the reason) instead of passing silently;
/// when PowerShell is required (CI, or SHELLY_REQUIRE_PWSH=1) it is not skipped and fails in <see cref="TestUtil.RequirePwsh"/>.</summary>
public sealed class PwshFactAttribute : FactAttribute
{
    public PwshFactAttribute()
    {
        if (TestUtil.Pwsh() == null && !TestUtil.PwshRequired)
            Skip = "PowerShell not found (install pwsh or set SHELLY_TEST_PWSH); with CI=true this test fails instead of being skipped";
    }
}
