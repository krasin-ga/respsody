using System;
using System.Globalization;
using Respsody.Benchmarks.NonScientific.Comparison;
using Xunit;

namespace Respsody.Tests;

public class ComparisonOptionsTests
{
    [Fact]
    public void DefaultComparisonIncludesBothOwnershipContracts()
    {
        var options = ComparisonOptions.Parse([]);
        Assert.Equal(new[] { "borrowed", "owned" }, options.Responses);
        Assert.Equal(new[] { 1, 32 }, options.Concurrency);
        Assert.True(options.MinimumSeconds > 0);
        Assert.True(options.MinimumCpuSeconds > 0);
        Assert.True(options.Rounds >= 7);
    }

    [Fact]
    public void ReproductionArgumentsAreCultureIndependent()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var options = ComparisonOptions.Parse(["--minimum-cpu-seconds", "0.5", "--payloads", "0,256,256",
                "--concurrency", "1,8", "--responses", "owned"]);
            Assert.Equal(0.5, options.MinimumCpuSeconds);
            Assert.Equal(new[] { 0, 256 }, options.Payloads);
            Assert.Equal(new[] { 1, 8 }, options.Concurrency);
            Assert.Equal(new[] { "owned" }, options.Responses);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("--rounds", "0")]
    [InlineData("--concurrency", "0")]
    [InlineData("--payloads", "-1")]
    [InlineData("--responses", "unknown")]
    [InlineData("--minimum-seconds", "NaN")]
    [InlineData("--minimum-seconds", "Infinity")]
    [InlineData("--minimum-seconds", "-1")]
    [InlineData("--minimum-seconds", "0,5")]
    [InlineData("--unknown", "1")]
    public void InvalidComparisonSettingsFailExplicitly(string name, string value)
    {
        Assert.Throws<ArgumentException>(() => ComparisonOptions.Parse([name, value]));
    }

    [Fact]
    public void MissingOptionValueFailsExplicitly()
    {
        Assert.Throws<ArgumentException>(() => ComparisonOptions.Parse(["--rounds"]));
    }
}
