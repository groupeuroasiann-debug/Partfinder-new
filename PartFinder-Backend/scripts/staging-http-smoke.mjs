/**
 * Smoke test hosted staging API (no secrets in stdout).
 * WMS_STAGING_API_BASE, WMS_SMOKE_EMAIL, WMS_SMOKE_PASSWORD via env.
 */
const base = (process.env.WMS_STAGING_API_BASE ?? '').replace(/\/$/, '');
const email = process.env.WMS_SMOKE_EMAIL?.trim();
const password = process.env.WMS_SMOKE_PASSWORD?.trim();
const orgCode = (process.env.WMS_STAGING_ORG_CODE ?? '900152').trim();

if (!base || !email || !password) {
  console.error('Set WMS_STAGING_API_BASE, WMS_SMOKE_EMAIL, WMS_SMOKE_PASSWORD');
  process.exit(2);
}

async function main() {
  const maintenance = await fetch(`${base}/api/public/platform/maintenance`);
  console.log('maintenance', maintenance.status, await maintenance.text());

  const loginRes = await fetch(`${base}/api/public/setup/invite-login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ orgCode, email, temporaryPassword: password }),
  });
  const loginText = await loginRes.text();
  let loginBody;
  try {
    loginBody = JSON.parse(loginText);
  } catch {
    loginBody = { raw: loginText.slice(0, 200) };
  }
  const token =
    loginBody?.accessToken ?? loginBody?.token ?? loginBody?.data?.accessToken;
  console.log('invite-login', loginRes.status, token ? 'token_received' : loginText.slice(0, 120));

  if (!token) process.exit(1);

  const preflight = await fetch(`${base}/api/wms/v1/staging-gate/preflight`, {
    method: 'POST',
    headers: {
      Authorization: `Bearer ${token}`,
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({ tenantLabel: '03A.2 hosted smoke' }),
  });
  const preJson = await preflight.json().catch(() => ({}));
  const data = preJson?.data ?? preJson;
  const sanitized = { ...data };
  delete sanitized.tenantUri;
  console.log('preflight', preflight.status, JSON.stringify(sanitized, null, 2));
  process.exit(preflight.ok ? 0 : 1);
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});