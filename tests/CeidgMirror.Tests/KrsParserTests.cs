using System.Net;
using CeidgMirror.Contracts;
using CeidgMirror.Infrastructure.Krs;

namespace CeidgMirror.Tests;

public sealed class KrsParserTests
{
    [Fact]
    public void ParseCurrentExcerpt_ExtractsCoreCompanyFields()
    {
        const string json = """
        {
          "odpis": {
            "naglowekA": {
              "rejestr": "RejP",
              "numerKRS": "0000120353",
              "dataRejestracjiWKRS": "26.06.2002",
              "dataOstatniegoWpisu": "16.05.2023",
              "oznaczenieSaduDokonujacegoOstatniegoWpisu": "SAD REJONOWY",
              "stanPozycji": 1
            },
            "dane": {
              "dzial1": {
                "danePodmiotu": {
                  "formaPrawna": "SPOLKA Z OGRANICZONA ODPOWIEDZIALNOSCIA",
                  "nazwa": "TEST SP. Z O.O.",
                  "identyfikatory": {
                    "regon": "63120582500000",
                    "nip": "7792015787"
                  }
                },
                "siedzibaIAdres": {
                  "siedziba": { "kraj": "POLSKA", "miejscowosc": "POZNAN" }
                }
              },
              "dzial2": {
                "reprezentacja": { "nazwaOrganu": "ZARZAD" }
              }
            }
          }
        }
        """;

        var response = new CeidgRawResponse(new Uri("https://api-krs.ms.gov.pl/api/krs/OdpisAktualny/0000120353?rejestr=P&format=json"), HttpStatusCode.OK, json, DateTimeOffset.UtcNow, "application/json");

        var record = KrsCurrentExcerptParser.Parse(response);

        Assert.Equal("0000120353", record.KrsNumber);
        Assert.Equal("RejP", record.RegisterType);
        Assert.Equal("7792015787", record.Nip);
        Assert.Equal("63120582500000", record.Regon);
        Assert.Equal("TEST SP. Z O.O.", record.Name);
        Assert.Equal(new DateOnly(2002, 6, 26), record.RegistrationDate);
        Assert.Contains("POZNAN", record.AddressJson);
        Assert.Contains("ZARZAD", record.RepresentativesJson);
    }

    [Fact]
    public void ParseKrsNumbers_CollectsDistinctTenDigitNumbers()
    {
        const string json = """
        [
          "Zmiana wpisu KRS 0000120353",
          { "opis": "Podmiot 0000335477 oraz duplikat 0000120353" },
          123
        ]
        """;

        var numbers = KrsBulletinParser.ParseKrsNumbers(json);

        Assert.Equal(new[] { "0000120353", "0000335477" }, numbers);
    }
}
