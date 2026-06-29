using System.Net;
using CeidgMirror.Application.Importing;
using CeidgMirror.Contracts;
using CeidgMirror.Infrastructure.Postgres;
using Npgsql;

namespace CeidgMirror.Tests;

public sealed class PostgresCompanyRecordStoreIntegrationTests
{
    [Fact]
    public async Task UpsertCompanyAsync_WhenEnabled_WritesSingleCompanyRecord()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("CEIDG_RUN_DB_TESTS"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var connectionString = Environment.GetEnvironmentVariable("CEIDG_TEST_CONNECTION_STRING")
            ?? "Host=localhost;Port=5433;Database=ceidg_mirror;Username=postgres;Password=postgres";

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        var store = new PostgresCompanyRecordStore(dataSource);
        var importRunId = await store.StartImportRunAsync("integration-test");
        const string ceidgId = "TEST-CEIDG-ID-0001";

        try
        {
            var indexItem = new CompanyIndexItem(
                ceidgId,
                "9999999999",
                "123456789",
                "https://test-dane.biznes.gov.pl/api/ceidg/v2/firma/TEST-CEIDG-ID-0001",
                """
                {"id":"TEST-CEIDG-ID-0001","wlasciciel":{"nip":"9999999999","regon":"123456789"}}
                """);

            var detailResponse = new CeidgRawResponse(
                new Uri("https://test-dane.biznes.gov.pl/api/ceidg/v2/firma/TEST-CEIDG-ID-0001"),
                HttpStatusCode.OK,
                """
                {
                  "firma": {
                    "id": "TEST-CEIDG-ID-0001",
                    "nazwa": "Test Company",
                    "status": "AKTYWNY",
                    "telefon": "123456789",
                    "email": "test@example.com",
                    "www": "https://example.com",
                    "pkdGlowny": "6201Z",
                    "pkd": ["6201Z", "6202Z"],
                    "wlasciciel": {
                      "imie": "Jan",
                      "nazwisko": "Testowy",
                      "nip": "9999999999",
                      "regon": "123456789"
                    },
                    "adresDzialalnosci": {
                      "miasto": "Warszawa",
                      "wojewodztwo": "MAZOWIECKIE"
                    },
                    "link": "https://test-dane.biznes.gov.pl/api/ceidg/v2/firma/TEST-CEIDG-ID-0001"
                  }
                }
                """,
                DateTimeOffset.UtcNow);

            await store.UpsertCompanyAsync(indexItem, detailResponse, importRunId);

            await using var command = dataSource.CreateCommand("""
                select name, owner_nip, raw_detail_payload->'firma'->>'email'
                from ceidg.company_records
                where ceidg_id = $1
                """);
            command.Parameters.AddWithValue(ceidgId);

            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("Test Company", reader.GetString(0));
            Assert.Equal("9999999999", reader.GetString(1));
            Assert.Equal("test@example.com", reader.GetString(2));
        }
        finally
        {
            await using var cleanupCompany = dataSource.CreateCommand("delete from ceidg.company_records where ceidg_id = $1");
            cleanupCompany.Parameters.AddWithValue(ceidgId);
            await cleanupCompany.ExecuteNonQueryAsync();

            await using var cleanupRun = dataSource.CreateCommand("delete from source.import_run where id = $1");
            cleanupRun.Parameters.AddWithValue(importRunId);
            await cleanupRun.ExecuteNonQueryAsync();
        }
    }
}

