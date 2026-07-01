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


    public async Task<ImportCheckpoint?> GetCheckpointAsync(
        string checkpointKey,
        CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand("""
            select checkpoint_key,
                   import_kind,
                   changes_from,
                   changes_to,
                   next_page,
                   next_item_index,
                   imported_count,
                   skipped_count,
                   failed_count,
                   completed,
                   last_company_id
            from source.import_checkpoint
            where checkpoint_key = $1
            """);
        command.Parameters.AddWithValue(checkpointKey);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ImportCheckpoint(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetFieldValue<DateOnly>(2),
            reader.IsDBNull(3) ? null : reader.GetFieldValue<DateOnly>(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetInt64(6),
            reader.GetInt64(7),
            reader.GetInt64(8),
            reader.GetBoolean(9),
            reader.IsDBNull(10) ? null : reader.GetString(10));
    }

    public async Task SaveCheckpointAsync(
        ImportCheckpoint checkpoint,
        object details,
        CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand("""
            insert into source.import_checkpoint (
                checkpoint_key,
                import_kind,
                changes_from,
                changes_to,
                next_page,
                next_item_index,
                imported_count,
                skipped_count,
                failed_count,
                completed,
                last_company_id,
                details,
                updated_at_utc
            )
            values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12::jsonb, now())
            on conflict (checkpoint_key) do update set
                import_kind = excluded.import_kind,
                changes_from = excluded.changes_from,
                changes_to = excluded.changes_to,
                next_page = excluded.next_page,
                next_item_index = excluded.next_item_index,
                imported_count = excluded.imported_count,
                skipped_count = excluded.skipped_count,
                failed_count = excluded.failed_count,
                completed = excluded.completed,
                last_company_id = excluded.last_company_id,
                details = excluded.details,
                updated_at_utc = now()
            """);

        Add(command, checkpoint.CheckpointKey);
        Add(command, checkpoint.ImportKind);
        Add(command, checkpoint.ChangesFrom);
        Add(command, checkpoint.ChangesTo);
        Add(command, checkpoint.NextPage);
        Add(command, checkpoint.NextItemIndex);
        Add(command, checkpoint.ImportedCount);
        Add(command, checkpoint.SkippedCount);
        Add(command, checkpoint.FailedCount);
        Add(command, checkpoint.Completed);
        Add(command, checkpoint.LastCompanyId);
        Add(command, JsonSerializer.Serialize(details));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> CompanyExistsAsync(
        string ceidgId,
        CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand("""
            select exists (
                select 1
                from ceidg.company_records
                where upper(ceidg_id) = upper($1)
            )
            """);
        command.Parameters.AddWithValue(ceidgId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is true;
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

    public async Task UpsertKrsCompanyAsync(
        KrsCompanyRecord record,
        Guid importRunId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        Guid? existingId = null;
        await using (var find = new NpgsqlCommand("""
            select id
            from ceidg.company_records
            where krs_number = $1
               or ($2 is not null and nip = $2)
               or ($3 is not null and regon = $3)
            order by case
                when krs_number = $1 then 1
                when $2 is not null and nip = $2 then 2
                when $3 is not null and regon = $3 then 3
                else 4
            end
            limit 1
            for update
            """, connection, transaction))
        {
            Add(find, record.KrsNumber);
            Add(find, record.Nip);
            Add(find, record.Regon);
            var result = await find.ExecuteScalarAsync(cancellationToken);
            if (result is Guid id)
            {
                existingId = id;
            }
        }

        if (existingId is not null)
        {
            await using var update = new NpgsqlCommand("""
                update ceidg.company_records
                set registry_sources = (
                        select array_agg(distinct source)
                        from unnest(array_append(coalesce(registry_sources, array[]::text[]), 'KRS')) as source
                    ),
                    krs_number = $2,
                    krs_register_type = $3,
                    krs_legal_form = $4,
                    krs_court_name = $5,
                    krs_registration_date = $6,
                    krs_last_entry_date = $7,
                    krs_status = $8,
                    krs_address = $9::jsonb,
                    krs_representatives = $10::jsonb,
                    raw_krs_payload = $11::jsonb,
                    krs_updated_at_utc = $12,
                    source_detail_url = coalesce(source_detail_url, $13),
                    last_detail_source_hash = coalesce(last_detail_source_hash, $14),
                    last_import_run_id = $15,
                    nip = coalesce(nullif(nip, ''), $16),
                    regon = coalesce(nullif(regon, ''), $17),
                    name = coalesce(nullif(name, ''), $18),
                    status = coalesce(nullif(status, ''), $19),
                    updated_at_utc = now()
                where id = $1
                """, connection, transaction);
            Add(update, existingId.Value);
            AddKrsParameters(update, record, importRunId);
            await update.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        await using (var insert = new NpgsqlCommand("""
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
                registry_sources,
                raw_index_payload,
                raw_detail_payload,
                raw_krs_payload,
                nip,
                regon,
                name,
                status,
                krs_number,
                krs_register_type,
                krs_legal_form,
                krs_court_name,
                krs_registration_date,
                krs_last_entry_date,
                krs_status,
                krs_address,
                krs_representatives,
                krs_updated_at_utc,
                extraction_version,
                extraction_warnings
            )
            values (
                $1, null, null, $2, null, $3, now(), now(), $4, array['KRS']::text[], null, null, $5::jsonb,
                $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16, $17::jsonb, $18::jsonb, $19, 1, '[]'::jsonb
            )
            """, connection, transaction))
        {
            Add(insert, Guid.NewGuid());
            Add(insert, record.SourceUri.ToString());
            Add(insert, Sha256(record.RawJson));
            Add(insert, importRunId);
            AddJson(insert, record.RawJson);
            Add(insert, record.Nip);
            Add(insert, record.Regon);
            Add(insert, record.Name);
            Add(insert, MapKrsStatusToCanonical(record.Status));
            Add(insert, record.KrsNumber);
            Add(insert, record.RegisterType);
            Add(insert, record.LegalForm);
            Add(insert, record.CourtName);
            Add(insert, record.RegistrationDate);
            Add(insert, record.LastEntryDate);
            Add(insert, record.Status);
            AddJson(insert, record.AddressJson);
            AddJson(insert, record.RepresentativesJson);
            Add(insert, record.FetchedAtUtc);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static void AddKrsParameters(NpgsqlCommand command, KrsCompanyRecord record, Guid importRunId)
    {
        Add(command, record.KrsNumber);
        Add(command, record.RegisterType);
        Add(command, record.LegalForm);
        Add(command, record.CourtName);
        Add(command, record.RegistrationDate);
        Add(command, record.LastEntryDate);
        Add(command, record.Status);
        AddJson(command, record.AddressJson);
        AddJson(command, record.RepresentativesJson);
        AddJson(command, record.RawJson);
        Add(command, record.FetchedAtUtc);
        Add(command, record.SourceUri.ToString());
        Add(command, Sha256(record.RawJson));
        Add(command, importRunId);
        Add(command, record.Nip);
        Add(command, record.Regon);
        Add(command, record.Name);
        Add(command, MapKrsStatusToCanonical(record.Status));
    }

    private static string? MapKrsStatusToCanonical(string? krsStatus) =>
        krsStatus?.Trim() switch
        {
            "1" => "AKTYWNY",
            "2" => "WYKRESLONY",
            _ => null
        };

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
