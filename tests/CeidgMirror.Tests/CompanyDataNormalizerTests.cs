using CeidgMirror.Infrastructure.Normalization;

namespace CeidgMirror.Tests;

public sealed class CompanyDataNormalizerTests
{
    [Theory]
    [InlineData("ŚLĄSKIE", "Śląskie")]
    [InlineData("małopolskie", "Małopolskie")]
    [InlineData("kujawsko-pomorskie", "Kujawsko-Pomorskie")]
    [InlineData("WARMIŃSKO-MAZURSKIE", "Warmińsko-Mazurskie")]
    public void NormalizeVoivodeship_ReturnsCanonicalDisplayName(string input, string expected)
    {
        Assert.Equal(expected, CompanyDataNormalizer.NormalizeVoivodeship(input));
    }

    [Theory]
    [InlineData("ŁUKASZ", "Łukasz")]
    [InlineData("JACKOWSKI", "Jackowski")]
    [InlineData("JASTRZĘBIE-ZDRÓJ", "Jastrzębie-Zdrój")]
    public void NormalizePersonAndPlaceNames_ReturnsPolishTitleCase(string input, string expected)
    {
        Assert.Equal(expected, CompanyDataNormalizer.NormalizePlaceName(input));
    }

    [Theory]
    [InlineData("UL. JANA PAWŁA II", "Jana Pawła II")]
    [InlineData("ulica Lipowa", "Lipowa")]
    [InlineData("Aleje Jerozolimskie", "Aleje Jerozolimskie")]
    public void NormalizeStreet_RemovesOnlyStreetPrefixAndKeepsNamesReadable(string input, string expected)
    {
        Assert.Equal(expected, CompanyDataNormalizer.NormalizeStreet(input));
    }

    [Theory]
    [InlineData("PL", "PL")]
    [InlineData("Polska", "PL")]
    [InlineData("POLSKA", "PL")]
    public void NormalizeCountryCode_ReturnsIso2Code(string input, string expected)
    {
        Assert.Equal(expected, CompanyDataNormalizer.NormalizeCountryCode(input));
    }

    [Theory]
    [InlineData("506 931 814", "+48506931814")]
    [InlineData("+48604129396, 730744300, 881661141", "+48604129396, +48730744300, +48881661141")]
    [InlineData("501222333 502333444", "+48501222333, +48502333444")]
    [InlineData("+48 34 3256023", "+48 34 325 60 23")]
    [InlineData("184777702", "+48 18 477 77 02")]
    public void NormalizePhoneList_FormatsMobileAndLandlinePolishNumbers(string input, string expected)
    {
        Assert.Equal(expected, CompanyDataNormalizer.NormalizePhoneList(input));
    }

    [Fact]
    public void NormalizeContactFields_ReturnsLowercaseDistinctValues()
    {
        Assert.Equal("pawel.gierczak@priceking.pl", CompanyDataNormalizer.NormalizeEmailList(" PAWEL.GIERCZAK@PRICEKING.PL "));
        Assert.Equal("https://www.priceking.com.pl", CompanyDataNormalizer.NormalizeWebsiteList("WWW.PRICEKING.COM.PL"));
    }

    [Theory]
    [InlineData("62.01.Z", "6201Z")]
    [InlineData(" 6201z ", "6201Z")]
    public void NormalizePkdCode_RemovesSeparatorsAndUppercases(string input, string expected)
    {
        Assert.Equal(expected, CompanyDataNormalizer.NormalizePkdCode(input));
    }
}
