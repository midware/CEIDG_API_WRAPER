using CeidgMirror.Contracts;
using CeidgMirror.Infrastructure.Ceidg;

namespace CeidgMirror.Tests;

public sealed class CeidgQueryBuilderTests
{
    [Fact]
    public void BuildCompaniesUri_UsesDocumentedLowercaseParameters()
    {
        var uri = CeidgQueryBuilder.BuildCompaniesUri(
            new Uri("https://test-dane.biznes.gov.pl/api/ceidg/v2/"),
            new CeidgFirmySearchRequest
            {
                Nip = ["1112223344"],
                City = ["Warszawa"],
                Status = [CeidgCompanyStatus.Aktywny],
                StartedFrom = new DateOnly(2020, 1, 1),
                Page = 2,
                Limit = 50
            });

        Assert.Equal(
            "https://test-dane.biznes.gov.pl/api/ceidg/v2/firmy?nip=1112223344&miasto=Warszawa&status=AKTYWNY&dataod=2020-01-01&page=2&limit=50",
            uri.ToString());
    }

    [Fact]
    public void BuildCompanyDetailsUri_UsesFirmaEndpointForFullDataByNip()
    {
        var uri = CeidgQueryBuilder.BuildCompanyDetailsUri(
            new Uri("https://test-dane.biznes.gov.pl/api/ceidg/v2/"),
            new CeidgCompanyDetailRequest { Nip = "2367852376" });

        Assert.Equal(
            "https://test-dane.biznes.gov.pl/api/ceidg/v2/firma?nip=2367852376",
            uri.ToString());
    }

    [Fact]
    public void BuildCompanyDetailsUri_RequiresNipOrRegon()
    {
        Assert.Throws<InvalidOperationException>(() =>
            CeidgQueryBuilder.BuildCompanyDetailsUri(
                new Uri("https://test-dane.biznes.gov.pl/api/ceidg/v2/"),
                new CeidgCompanyDetailRequest()));
    }

    [Fact]
    public void BuildChangesUri_UsesDateRangeAndPaging()
    {
        var uri = CeidgQueryBuilder.BuildChangesUri(
            new Uri("https://test-dane.biznes.gov.pl/api/ceidg/v2"),
            new DateOnly(2021, 9, 1),
            new DateOnly(2021, 9, 30),
            page: 1,
            limit: 500);

        Assert.Equal(
            "https://test-dane.biznes.gov.pl/api/ceidg/v2/zmiana?dataod=2021-09-01&datado=2021-09-30&page=1&limit=500",
            uri.ToString());
    }
}
