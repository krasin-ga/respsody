using System.Globalization;
using Respsody.Library;
using Xunit;

namespace Respsody.Tests;

public class ProtocolTokenCultureRegressionTests
{
    [Theory]
    [InlineData("en-US")]
    [InlineData("tr-TR")]
    public void ProtocolTokensUseOrdinalCaseComparison(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            Assert.True("PRIMARY".EqualTo("primary", "master"));
            Assert.True("REPLICA".EqualTo("replica", "slave"));
            Assert.False("PRıMARY".EqualTo("primary"));
            Assert.False("pr\0imary".EqualTo("primary"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
