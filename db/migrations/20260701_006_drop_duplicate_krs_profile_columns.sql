-- Drop KRS source columns that duplicate canonical company profile fields.
-- Values are preserved in legal_form, status, business_address_*, electronic_delivery_address and raw_krs_payload.

drop index if exists ceidg.ix_company_records_krs_legal_form;
drop index if exists ceidg.ix_company_records_krs_status;

alter table ceidg.company_records
    drop column if exists krs_legal_form,
    drop column if exists krs_status,
    drop column if exists krs_address;
