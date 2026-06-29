using CeidgMirror.Infrastructure.Importing;

namespace CeidgMirror.Tests;

public sealed class CeidgFirmyResponseParserTests
{
    [Fact]
    public void ParseCompanies_ExtractsIdentifiersNeededForDetailHydration()
    {
        const string json = """
            {
              "firmy": [
                {
                  "id": "31F87519-9395-4FCF-8E19-6D5C0522FA7A",
                  "link": "https://test-dane.biznes.gov.pl/api/ceidg/v2/firma/31F87519-9395-4FCF-8E19-6D5C0522FA7A",
                  "wlasciciel": {
                    "nip": "2367852376",
                    "regon": "123456789"
                  }
                }
              ]
            }
            """;

        var items = CeidgFirmyResponseParser.ParseCompanies(json);

        Assert.Single(items);
        Assert.Equal("31F87519-9395-4FCF-8E19-6D5C0522FA7A", items[0].CeidgId);
        Assert.Equal("2367852376", items[0].Nip);
        Assert.Equal("123456789", items[0].Regon);
        Assert.Contains("/firma/31F87519", items[0].DetailLink);
    }
}
