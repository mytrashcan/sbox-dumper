using SboxDumper.Readers;
using Xunit;

namespace SboxDumper.Tests;

public class FieldReadersTests
{
    [Theory]
    [InlineData("<SteamId>k__BackingField", "SteamId")]
    [InlineData("<GameObject>k__BackingField", "GameObject")]
    [InlineData("<IsThirdPersonPreferred>k__BackingField", "IsThirdPersonPreferred")]
    [InlineData("_name", "name")]
    [InlineData("_gameTransform", "gameTransform")]
    [InlineData("plainField", "plainField")]
    [InlineData("", "")]
    [InlineData("<NoClosing", "<NoClosing")]
    public void CleanFieldName_StripsBackingFieldAndUnderscorePrefixes(string raw, string expected)
        => Assert.Equal(expected, FieldReaders.CleanFieldName(raw));
}
