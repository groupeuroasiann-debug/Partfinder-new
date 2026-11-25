
import { MongoClient } from 'mongodb';
import { readFileSync, existsSync } from 'fs';
import { parse } from 'dotenv';
import { resolve, dirname } from 'path';
import { fileURLToPath } from 'url';
import { configureAtlasFriendlyDns } from './mongodb-atlas-dns.mjs';

// Configure MongoDB Atlas DNS resolution.
configureAtlasFriendlyDns();

// Resolve the backend project root.
const root = resolve(
  dirname(fileURLToPath(import.meta.url)),
  '..',
);

// Load environment variables safely.
// Priority:
// 1. Existing environment variables
// 2. .env.local
// 3. .env
function loadEnvironment() {
  const files = ['.env', '.env.local'];

  for (const file of files) {
    const filePath = resolve(root, file);

    if (!existsSync(filePath)) {
      continue;
    }

    const values = parse(readFileSync(filePath));

    for (const [key, value] of Object.entries(values)) {
      // Do not overwrite environment variables already set.
      // .env.local takes precedence over .env for file values.
      if (
        process.env[key] === undefined ||
        (file === '.env.local' &&
          process.env[key] === valuesFromEnv[key])
      ) {
        process.env[key] = value;
      }
    }

    console.log(`Loaded environment: ${file}`);
  }
}

// Track .env values so .env.local can override them,
// without overwriting environment variables supplied by the host.
const valuesFromEnv = {};

const envPath = resolve(root, '.env');
if (existsSync(envPath)) {
  Object.assign(valuesFromEnv, parse(readFileSync(envPath)));
}

loadEnvironment();

// Validate MongoDB URI.
const mongoUri = process.env.MONGODB_URI;

if (!mongoUri) {
  console.error(
    'ERROR: MONGODB_URI is missing from the environment.',
  );
  process.exit(1);
}

if (
  !mongoUri.startsWith('mongodb://') &&
  !mongoUri.startsWith('mongodb+srv://')
) {
  console.error('ERROR: Invalid MongoDB URI scheme.');
  process.exit(1);
}

// Validate organization code.
const code = (process.argv[2] ?? '900152').trim();

if (!/^\d{6}$/.test(code)) {
  console.error(
    'ERROR: Organization code must contain 6 digits.',
  );
  process.exit(1);
}

const client = new MongoClient(mongoUri, {
  serverSelectionTimeoutMS: 15000,
});

try {
  // Connect to platform MongoDB.
  await client.connect();

  console.log('MongoDB connection: SUCCESS');

  const doc = await client
    .db()
    .collection('organizations')
    .findOne({ orgCode: code });

  if (!doc) {
    console.log(`Organization ${code}: NOT FOUND`);
    process.exitCode = 1;
  } else {
    const tenantUri = String(
      doc.orgDatabaseUri ?? '',
    ).trim();

    let dbName = null;

    if (tenantUri) {
      try {
        const pseudo = tenantUri
          .replace(/^mongodb\+srv:\/\//, 'https://')
          .replace(/^mongodb:\/\//, 'http://');

        dbName =
          new URL(pseudo).pathname.replace(/^\//, '') ||
          null;
      } catch {
        dbName = '<parse-failed>';
      }
    }

    // Print only sanitized organization details.
    const result = {
      orgCode: doc.orgCode,
      name: doc.name,
      wmsEnabled: doc.wmsEnabled === true,
      status: doc.status,
      plan: doc.plan,
      hasTenantUri: Boolean(tenantUri),
      tenantDatabaseName: dbName,
      platformHostOnly: tenantUri
        ? 'atlas-cluster-redacted'
        : null,
    };

    console.log(
      JSON.stringify(result, null, 2),
    );

    if (
      code === '900152' &&
      dbName !== 'wms_staging_acceptance_900152'
    ) {
      console.error(
        'WARNING: Staging tenant database does not match the expected name.',
      );
      process.exitCode = 1;
    }
  }
} catch (error) {
  console.error('MongoDB organization lookup failed.');

  // Avoid printing connection strings or secrets.
  console.error(
    `Error type: ${error?.name ?? 'UnknownError'}`,
  );

  process.exitCode = 1;
} finally {
  await client.close();
}
