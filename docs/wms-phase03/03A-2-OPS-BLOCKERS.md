# Phase 03A.2 — Staging provisioning blockers

**Updated:** 2026-11-19 (Vercel staging verification)

## Resolved in engineering session

| Action | Result |
|--------|--------|
| Vercel **`partfinder-backend-staging`** | **Ready** — `https://partfinder-backend-staging.vercel.app` (public API, no SSO block on smoke tests) |
| Platform org **900152** | **PASS** — `wms_staging_acceptance_900152`, `wmsEnabled: true` |
| Hosted `POST …/staging-gate/preflight` | **PASS** txn probes — `stagingStockPosting: false` until env flip |
| Hosted `invite-login` (admin + A/B) | **PASS** — see [`evidence/03a2-hosted-auth-evidence.json`](./evidence/03a2-hosted-auth-evidence.json) |
| `npm run wms:mongodb-dns-preflight` | **PASS** |
| `npm run wms:03a2-staging-acceptance` | **PASS** (in-process harness) |

## Still blocking **ACCEPTED**

| # | Blocker | Owner action |
|---|---------|--------------|
| 1 | `WMS_STAGING_STOCK_POSTING=true` on **staging Vercel only** + redeploy | From linked `PartFinder-Staging-Deploy\PartFinder-Backend`: `vercel env rm/add` then `vercel deploy --prod --yes` |
| 2 | WinUI **100-filter** checklist | Run `PartFinder-Backend/scripts/winui-staging-acceptance.ps1`; `PARTFINDER_STAGING_API=1`; attach §12 screenshots |
| 3 | §11 engineering / QA sign-off | Names and dates in acceptance report |

**Do not** enable `WMS_LIVE_STOCK_POSTING` on production `partfinder-new`.

## Reference

- Deployment: [`03A-2-staging-deployment-package.md`](./03A-2-staging-deployment-package.md)
- Identity bootstrap: `npm run wms:staging-hosted-identities` (requires `WMS_STAGING_TEST_PASSWORD`)