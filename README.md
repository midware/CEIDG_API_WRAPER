# CEIDG_API_WRAPER

Private C#/.NET project for building a local PostgreSQL analytical mirror of CEIDG data.

The target product is a data platform/API that can ingest CEIDG public business data, preserve raw source payloads, normalize the most important entities into relational tables, and expose clean query/analysis endpoints for downstream services.

## Current Status

This repository currently contains the CTO plan and source API documentation copies. Implementation should start with an ingestion proof of concept against CEIDG API v2, then evolve into a production-grade import pipeline.

## Source APIs

- CEIDG API v2 Hurtowni Danych: `https://dane.biznes.gov.pl/api/ceidg/v2`
- Test CEIDG API v2: `https://test-dane.biznes.gov.pl/api/ceidg/v2`
- Legacy CEIDG DataStore SOAP API: documented in `OpisAPINew.pdf`

## Key Constraints

- API v2 uses JWT bearer authentication in the `Authorization` header.
- API v2 enforces request limits: 50 requests per 3 minutes and 1000 requests per 60 minutes.
- API v2 exposes paginated list endpoints and a change endpoint for incremental synchronization.
- The local PostgreSQL database must retain source fidelity, not only a lossy projection.

## Documents

- [CTO plan](docs/CTO_PLAN.md)
