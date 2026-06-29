using CeidgMirror.Infrastructure.Importing;

namespace CeidgMirror.Tests;

public sealed class CeidgZmianaResponseParserTests
{
    [Fact]
    public void ParseCompanyIds_ReadsIdentifiersFromV3Response()
    {
        const string json = """
            {
              "identyfikatoryWpisow": [
                "793a9a04-9734-4adb-b6f2-074c20ba36d9",
                "e1a340bb-4fb4-4dd3-864d-25144b765adb"
              ],
              "count": 2
            }
            """;

        var ids = CeidgZmianaResponseParser.ParseCompanyIds(json);

        Assert.Equal(2, ids.Count);
        Assert.Equal("793a9a04-9734-4adb-b6f2-074c20ba36d9", ids[0]);
        Assert.Equal("e1a340bb-4fb4-4dd3-864d-25144b765adb", ids[1]);
    }
}