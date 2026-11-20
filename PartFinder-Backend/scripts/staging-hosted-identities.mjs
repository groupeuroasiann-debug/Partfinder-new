/**
 * Upsert staging-only login identities for org 900152 (tenant DB).
 * Does NOT delete inventory, idempotency keys, or unrelated collections.
 *
 * Requires: MONGODB_URI, WMS_STAGING_TEST_PASSWORD (min 12 chars)
 * Optional: WMS_STAGING_ORG_CODE (default 900152)
 *
 * Emails (stable):
 *   wms-admin-staging@partfinder.acceptance
 *   operator-a-staging@partfinder.acceptance
 *   operator-b-staging@partfinder.acceptance
 */
import { MongoClient } from 'mongodb';
import bcrypt from 'bcrypt';
import { pbkdf2Sync, randomBytes } from 'crypto';

function hashTemporaryPassword(password) {
  const iterations = 310_000;
  const salt = randomBytes(16);
  const hash = pbkdf2Sync(password, salt, iterations, 32, 'sha256');
  return `pbkdf2$${iterations}$${salt.toString('base64')}$${hash.toString('base64')}`;
}
import { readFileSync, existsSync } from 'fs';
import { parse } from 'dotenv';
import { resolve, dirname } from 'path';
import { fileURLToPath } from 'url';
import { configureAtlasFriendlyDns } from './mongodb-atlas-dns.mjs';

configureAtlasFriendlyDns();

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
for (const file of ['.env', '.env.local']) {
  const p = resolve(root, file);
  if (existsSync(p)) {
    for (const [k, v] of Object.entries(parse(readFileSync(p)))) {
      if (process.env[k] === undefined) process.env[k] = v;
    }
  }
}

const ORG = (process.env.WMS_STAGING_ORG_CODE ?? '900152').trim();
const password = process.env.WMS_STAGING_TEST_PASSWORD?.trim();
if (!process.env.MONGODB_URI?.trim()) {
  console.error('MONGODB_URI required');
  process.exit(1);
}
if (!password || password.length < 12) {
  console.error('WMS_STAGING_TEST_PASSWORD required (min 12 characters)');
  process.exit(2);
}

const emails = {
  admin: 'wms-admin-staging@partfinder.acceptance',
  operatorA: 'operator-a-staging@partfinder.acceptance',
  operatorB: 'operator-b-staging@partfinder.acceptance',
};

const client = new MongoClient(process.env.MONGODB_URI, {
  serverSelectionTimeoutMS: 20000,
});

try {
  await client.connect();
  const platform = client.db().collection('organizations');
  const org = await platform.findOne({ orgCode: ORG });
  if (!org?.orgDatabaseUri?.trim()) {
    console.error(`Org ${ORG} missing orgDatabaseUri`);
    process.exit(1);
  }
  const uri = org.orgDatabaseUri.trim();
  const tenantClient = new MongoClient(uri, { serverSelectionTimeoutMS: 20000 });
  await tenantClient.connect();
  const tenantDb = tenantClient.db();
  const adminHash = await bcrypt.hash(password, 10);
  const inviteHash = hashTemporaryPassword(password);
  const now = new Date();

  await tenantDb.collection('org_admin_users').updateOne(
    { email: emails.admin },
    {
      $set: {
        email: emails.admin,
        passwordHash: adminHash,
        name: 'WMS Staging Admin',
        updatedAt: now,
      },
      $setOnInsert: { createdAt: now },
    },
    { upsert: true },
  );

  for (const [key, name] of [
    ['operatorA', 'Operator A'],
    ['operatorB', 'Operator B'],
  ]) {
    const email = emails[key];
    await tenantDb.collection('org_app_users').updateOne(
      { emailNormalized: email },
      {
        $set: {
          name,
          email,
          emailNormalized: email,
          role: 'Employee',
          partsAllTemplates: false,
          allowedTemplateIds: [],
          temporaryPasswordHash: inviteHash,
          invitedAtUtc: now,
        },
      },
      { upsert: true },
    );
  }

  console.log(
    JSON.stringify(
      {
        ok: true,
        orgCode: ORG,
        tenantDatabase: tenantDb.databaseName,
        emails,
        passwordConfigured: true,
      },
      null,
      2,
    ),
  );
  await tenantClient.close();
} finally {
  await client.close();
}