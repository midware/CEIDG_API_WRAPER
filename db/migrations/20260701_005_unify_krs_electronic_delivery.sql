-- Copy KRS BAE electronic delivery addresses into the canonical electronic_delivery_address column.

update ceidg.company_records
set electronic_delivery_address = coalesce(
        nullif(electronic_delivery_address, ''),
        raw_krs_payload #>> '{odpis,dane,dzial1,siedzibaIAdres,adresDoDoreczenElektronicznychWpisanyDoBAE}',
        krs_address #>> '{adresDoDoreczenElektronicznychWpisanyDoBAE}'
    )
where raw_krs_payload is not null
   or krs_address is not null;
