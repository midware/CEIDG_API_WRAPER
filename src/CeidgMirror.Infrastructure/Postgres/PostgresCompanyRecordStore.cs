using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CeidgMirror.Application.Importing;
using CeidgMirror.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace CeidgMirror.Infrastructure.Postgres;

public sealed class PostgresCompanyRecordStore(NpgsqlDataSource dataSource) : ICompanyRecordStore
{
    public async Task<Guid> StartImportRunAsync(string importKind, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        await using var command = dataSource.CreateCommand("""
            insert into source.import_run (id, import_kind, started_at_utc, status)
            values ($1, $2, now(), 'running')
            """);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(importKind);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return id;
    }

    public async Task CompleteImportRunAsync(
        Guid importRunId,
        string status,
        object details,
        CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand("""
            update source.import_run
            set finished_at_utc = now(), status = $2, details = $3::jsonb
            where id = $1
            """);
        command.Parameters.AddWithValue(importRunId);
        command.Parameters.AddWithValue(status);
        command.Parameters.AddWithValue(JsonSerializer.Serialize(details));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertCompanyAsync(
        CompanyIndexItem? indexItem,
        CeidgRawResponse detailResponse,
        Guid importRunId,
        CancellationToken cancellationToken = default)
    {
        var detailJson = detailResponse.Content;
        using var document = JsonDocument.Parse(detailJson);
        var firma = ExtractFirmaElement(document.RootElement);

        var ceidgId = ReadString(firma, "id") ?? indexItem?.CeidgId ?? ReadString(firma, "link") ?? throw new InvalidOperationException("CEIDG detail payload does not contain id or link.");
        var owner = TryGetProperty(firma, "wlasciciel", out var ownerElement) ? ownerElement : default;
        var address = TryGetProperty(firma, "adresDzialalnosci", out var addressElement) ? addressElement : default;

        await using var command = dataSource.CreateCommand("""
            insert into ceidg.company_records (
                id,
                ceidg_id,
                source_index_url,
                source_detail_url,
                last_index_source_hash,
                last_detail_source_hash,
                first_seen_at_utc,
                updated_at_utc,
                last_import_run_id,
                raw_index_payload,
                raw_detail_payload,
                nip,
                regon,
                name,
                status,
                status_number,
                phone,
                email,
                website,
                electronic_delivery_address,
                other_contact_form,
                marital_property_community,
                marital_property_community_ended_on,
                started_on,
                suspended_on,
                ended_on,
                removed_on,
                resumed_on,
                owner_death_on,
                succession_management_established_on,
                succession_management_expired_on,
                detail_link,
                owner_first_name,
                owner_last_name,
                owner_nip,
                owner_nip_revoked,
                owner_nip_invalidated,
                owner_regon,
                business_address_country,
                business_address_voivodeship,
                business_address_county,
                business_address_municipality,
                business_address_city,
                business_address_street,
                business_address_building,
                business_address_unit,
                business_address_postal_code,
                business_address_post_office_box,
                business_address_unusual_place_description,
                business_address_recipient,
                business_address_terc,
                business_address_simc,
                business_address_ulic,
                main_pkd_code,
                citizenships,
                legal_removal_bases,
                correspondence_address,
                pkd_codes,
                civil_partnerships,
                additional_business_addresses,
                bans,
                bankruptcy,
                succession_manager,
                professional_qualifications,
                permissions,
                restrictions,
                legal_capacity_restrictions,
                extraction_version,
                extraction_warnings
            )
            values (
                $1, $2, $3, $4, $5, $6, now(), now(), $7, $8::jsonb, $9::jsonb,
                $10, $11, $12, $13, $14, $15, $16, $17, $18, $19, $20, $21, $22, $23, $24, $25, $26, $27, $28, $29,
                $30, $31, $32, $33, $34, $35, $36, $37, $38, $39, $40, $41, $42, $43, $44, $45, $46, $47, $48, $49,
                $50, $51, $52, $53::jsonb, $54::jsonb, $55::jsonb, $56::jsonb, $57::jsonb, $58::jsonb, $59::jsonb,
                $60::jsonb, $61::jsonb, $62::jsonb, $63::jsonb, $64::jsonb, $65::jsonb, 1, '[]'::jsonb
            )
            on conflict (ceidg_id) do update set
                source_index_url = excluded.source_index_url,
                source_detail_url = excluded.source_detail_url,
                last_index_source_hash = excluded.last_index_source_hash,
                last_detail_source_hash = excluded.last_detail_source_hash,
                updated_at_utc = now(),
                last_import_run_id = excluded.last_import_run_id,
                raw_index_payload = excluded.raw_index_payload,
                raw_detail_payload = excluded.raw_detail_payload,
                nip = excluded.nip,
                regon = excluded.regon,
                name = excluded.name,
                status = excluded.status,
                status_number = excluded.status_number,
                phone = excluded.phone,
                email = excluded.email,
                website = excluded.website,
                electronic_delivery_address = excluded.electronic_delivery_address,
                other_contact_form = excluded.other_contact_form,
                marital_property_community = excluded.marital_property_community,
                marital_property_community_ended_on = excluded.marital_property_community_ended_on,
                started_on = excluded.started_on,
                suspended_on = excluded.suspended_on,
                ended_on = excluded.ended_on,
                removed_on = excluded.removed_on,
                resumed_on = excluded.resumed_on,
                owner_death_on = excluded.owner_death_on,
                succession_management_established_on = excluded.succession_management_established_on,
                succession_management_expired_on = excluded.succession_management_expired_on,
                detail_link = excluded.detail_link,
                owner_first_name = excluded.owner_first_name,
                owner_last_name = excluded.owner_last_name,
                owner_nip = excluded.owner_nip,
                owner_nip_revoked = excluded.owner_nip_revoked,
                owner_nip_invalidated = excluded.owner_nip_invalidated,
                owner_regon = excluded.owner_regon,
                business_address_country = excluded.business_address_country,
                business_address_voivodeship = excluded.business_address_voivodeship,
                business_address_county = excluded.business_address_county,
                business_address_municipality = excluded.business_address_municipality,
                business_address_city = excluded.business_address_city,
                business_address_street = excluded.business_address_street,
                business_address_building = excluded.business_address_building,
                business_address_unit = excluded.business_address_unit,
                business_address_postal_code = excluded.business_address_postal_code,
                business_address_post_office_box = excluded.business_address_post_office_box,
                business_address_unusual_place_description = excluded.business_address_unusual_place_description,
                business_address_recipient = excluded.business_address_recipient,
                business_address_terc = excluded.business_address_terc,
                business_address_simc = excluded.business_address_simc,
                business_address_ulic = excluded.business_address_ulic,
                main_pkd_code = excluded.main_pkd_code,
                citizenships = excluded.citizenships,
                legal_removal_bases = excluded.legal_removal_bases,
                correspondence_address = excluded.correspondence_address,
                pkd_codes = excluded.pkd_codes,
                civil_partnerships = excluded.civil_partnerships,
                additional_business_addresses = excluded.additional_business_addresses,
                bans = excluded.bans,
                bankruptcy = excluded.bankruptcy,
                succession_manager = excluded.succession_manager,
                professional_qualifications = excluded.professional_qualifications,
                permissions = excluded.permissions,
                restrictions = excluded.restrictions,
                legal_capacity_restrictions = excluded.legal_capacity_restrictions,
                extraction_version = excluded.extraction_version,
                extraction_warnings = excluded.extraction_warnings
            """);

        Add(command, Guid.NewGuid());
        Add(command, ceidgId);
        Add(command, indexItem?.DetailLink);
        Add(command, detailResponse.RequestUri.ToString());
        Add(command, indexItem is null ? null : Sha256(indexItem.RawJson));
        Add(command, Sha256(detailJson));
        Add(command, importRunId);
        AddJson(command, indexItem?.RawJson);
        AddJson(command, detailJson);
        Add(command, ReadString(firma, "nip") ?? ReadString(owner, "nip") ?? indexItem?.Nip);
        Add(command, ReadString(firma, "regon") ?? ReadString(owner, "regon") ?? indexItem?.Regon);
        Add(command, ReadString(firma, "nazwa"));
        Add(command, ReadString(firma, "status"));
        Add(command, ReadInt(firma, "numerStatusu"));
        Add(command, ReadString(firma, "telefon"));
        Add(command, ReadString(firma, "email"));
        Add(command, ReadString(firma, "www"));
        Add(command, ReadString(firma, "adresDoreczenElektronicznych"));
        Add(command, ReadString(firma, "innaFormaKonaktu") ?? ReadString(firma, "innaFormaKontaktu"));
        Add(command, ReadInt(firma, "wspolnoscMajatkowa"));
        Add(command, ReadDate(firma, "wspolnoscMajatkowaDataUstania"));
        Add(command, ReadDate(firma, "dataRozpoczecia"));
        Add(command, ReadDate(firma, "dataZawieszenia"));
        Add(command, ReadDate(firma, "dataZakonczenia"));
        Add(command, ReadDate(firma, "dataWykreslenia"));
        Add(command, ReadDate(firma, "dataWznowienia"));
        Add(command, ReadDate(firma, "dataZgonu"));
        Add(command, ReadDate(firma, "zarzadSukcesyjnyDataUstanowienia"));
        Add(command, ReadDate(firma, "zarzadSukcesyjnyDataWygasniecia"));
        Add(command, ReadString(firma, "link"));
        Add(command, ReadString(owner, "imie"));
        Add(command, ReadString(owner, "nazwisko"));
        Add(command, ReadString(owner, "nip"));
        Add(command, ReadBool(owner, "nipUchylony"));
        Add(command, ReadBool(owner, "nipUniewazniony"));
        Add(command, ReadString(owner, "regon"));
        Add(command, ReadString(address, "kraj"));
        Add(command, ReadString(address, "wojewodztwo"));
        Add(command, ReadString(address, "powiat"));
        Add(command, ReadString(address, "gmina"));
        Add(command, ReadString(address, "miasto"));
        Add(command, ReadString(address, "ulica"));
        Add(command, ReadString(address, "budynek"));
        Add(command, ReadString(address, "lokal"));
        Add(command, ReadString(address, "kod"));
        Add(command, ReadString(address, "skrytkaPocztowa"));
        Add(command, ReadString(address, "opisNietypowegoMiejsca"));
        Add(command, ReadString(address, "adresat"));
        Add(command, ReadString(address, "terc"));
        Add(command, ReadString(address, "simc"));
        Add(command, ReadString(address, "ulic"));
        Add(command, ReadPkdCode(firma, "pkdGlowny"));
        AddJson(command, ReadRaw(firma, "obywatelstwo") ?? ReadRaw(firma, "obywatelstwa"));
        AddJson(command, ReadRaw(firma, "podstawyPrawneWykreslenia"));
        AddJson(command, ReadRaw(firma, "adresKorespondencyjny"));
        AddJson(command, ReadRaw(firma, "pkd"));
        AddJson(command, ReadRaw(firma, "spolka"));
        AddJson(command, ReadRaw(firma, "adresyDzialanosciDodatkowe") ?? ReadRaw(firma, "adresyDzialalnosciDodatkowe"));
        AddJson(command, ReadRaw(firma, "zakazy"));
        AddJson(command, ReadRaw(firma, "upadlosc"));
        AddJson(command, ReadRaw(firma, "zarzadcaSukcesyjny"));
        AddJson(command, ReadRaw(firma, "kwalifikacjeZawodowe"));
        AddJson(command, ReadRaw(firma, "uprawnienia"));
        AddJson(command, ReadRaw(firma, "ograniczenia"));
        AddJson(command, ReadRaw(firma, "ograniczeniaZdolnosciPrawnej"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertReportPayloadAsync(
        CeidgReportDescriptor report,
        CeidgRawResponse payloadResponse,
        Guid importRunId,
        CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            report = new
            {
                report.GeneratedReportId,
                report.ReportName,
                report.ReportDescription,
                report.ReportParameters,
                report.FileType,
                report.GeneratedOn,
                report.GeneratedOnOnlyDate,
                report.RawJson
            },
            download = new
            {
                payloadResponse.ContentType,
                payloadResponse.Content,
                payloadResponse.FetchedAtUtc
            }
        });

        await using var command = dataSource.CreateCommand("""
            insert into source.report_payload (
                id,
                ceidg_report_id,
                request_uri,
                status_code,
                content_hash,
                payload,
                fetched_at_utc,
                import_run_id
            )
            values ($1, $2, $3, $4, $5, $6::jsonb, $7, $8)
            on conflict (ceidg_report_id) do update set
                request_uri = excluded.request_uri,
                status_code = excluded.status_code,
                content_hash = excluded.content_hash,
                payload = excluded.payload,
                fetched_at_utc = excluded.fetched_at_utc,
                import_run_id = excluded.import_run_id
            """);

        Add(command, Guid.NewGuid());
        Add(command, report.GeneratedReportId);
        Add(command, payloadResponse.RequestUri.ToString());
        Add(command, (int)payloadResponse.StatusCode);
        Add(command, Sha256(payloadResponse.Content));
        AddJson(command, payload);
        Add(command, payloadResponse.FetchedAtUtc);
        Add(command, importRunId);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    private static JsonElement ExtractFirmaElement(JsonElement root)
    {
        if (!TryGetProperty(root, "firma", out var firmaElement))
        {
            return root;
        }

        if (firmaElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in firmaElement.EnumerateArray())
            {
                return item;
            }

            throw new InvalidOperationException("CEIDG detail payload contains an empty firma array.");
        }

        return firmaElement;
    }
    private static void Add(NpgsqlCommand command, object? value) => command.Parameters.AddWithValue(value ?? DBNull.Value);

    private static void AddJson(NpgsqlCommand command, string? json)
    {
        var parameter = command.Parameters.AddWithValue(json ?? "null");
        parameter.NpgsqlDbType = NpgsqlDbType.Jsonb;
    }

    private static string Sha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string? ReadPkdCode(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Object && TryGetProperty(value, "kod", out var code))
        {
            return code.ValueKind == JsonValueKind.String ? code.GetString() : code.ToString();
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }
    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return int.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static bool? ReadBool(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when value.TryGetInt32(out var number) => number != 0,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            JsonValueKind.String when int.TryParse(value.GetString(), out var number) => number != 0,
            _ => null
        };
    }

    private static DateOnly? ReadDate(JsonElement element, string propertyName)
    {
        var value = ReadString(element, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateOnly.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string? ReadRaw(JsonElement element, string propertyName) =>
        TryGetProperty(element, propertyName, out var value) && value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined
            ? value.GetRawText()
            : null;

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        value = default;
        return false;
    }
}
