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

    [Fact]
    public void NormalizeStreet_KeepsCommonPrefixLowercaseAndRomanNumeralsUppercase()
    {
        Assert.Equal("ul. Jana Pawła II", CompanyDataNormalizer.NormalizeStreet("UL. JANA PAWŁA II"));
    }

    [Theory]
    [InlineData("506 931 814", "+48506931814")]
    [InlineData("+48604129396, 730744300, 881661141", "+48604129396, +48730744300, +48881661141")]
    [InlineData("501222333 502333444", "+48501222333, +48502333444")]
    [InlineData("+48 34 3256023", "+48343256023")]
    public void NormalizePhoneList_ReturnsCommaSeparatedE164PolishNumbers(string input, string expected)
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
