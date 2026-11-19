
/**
 * Phase 03A.2 named-tenant API acceptance test.
 *
 * SAFETY:
 * - Skipped unless WMS_RUN_STAGING_ACCEPTANCE=true and MONGODB_URI is set.
 * - Writes require WMS_ACCEPTANCE_ALLOW_WRITES=YES_900152.
 * - Requires staging deployment tier and staging stock posting explicitly enabled.
 * - Verifies the existing platform organization's tenant mapping without changing it.
 * - Uses fresh, run-specific warehouse/owners/locations/SKU/grants and keys.
 * - Never deletes existing records or resets shared warehouse state.
 * - Leaves test records behind for review; remove only with a separately reviewed cleanup.
 * - API-service evidence is NOT hosted HTTP / real WinUI acceptance.
 */
import '../test-utils/mongodb-atlas-dns';
import { existsSync, readFileSync, mkdirSync, writeFileSync } from 'fs';
import { join } from 'path';
import { parse } from 'dotenv';
import { afterAll, beforeAll, describe, expect, it } from '@jest/globals';
import { ForbiddenException } from '@nestjs/common';
import { Test, type TestingModule } from '@nestjs/testing';
import { MongoClient } from 'mongodb';
import { Types } from 'mongoose';
import { TenantMongoService } from '../setup/tenant-mongo.service';
import { WmsDiagnosticsService } from './services/wms-diagnostics.service';
import { WmsInventoryPostingService } from './services/wms-inventory-posting.service';
import { WmsReceivingService } from './services/wms-receiving.service';
import { WmsStagingGateService } from './services/wms-staging-gate.service';
import { WmsTenantCollectionsService } from './services/wms-tenant-collections.service';
import { WmsTransactionGuardService } from './services/wms-transaction-guard.service';
import type { WmsRequestContext } from './wms.types';
import { WMS_COLLECTIONS } from './wms.types';

// Load .env and .env.local, but NEVER override variables explicitly supplied
// by the shell, CI runner, or hosting platform.
const backendRoot = join(__dirname, '..', '..');
const fileEnv: Record<string, string> = {};

for (const filename of ['.env', '.env.local']) {
  const path = join(backendRoot, filename);

  if (existsSync(path)) {
    Object.assign(fileEnv, parse(readFileSync(path)));
  }
}

for (const [key, value] of Object.entries(fileEnv)) {
  if (process.env[key] === undefined) {
    process.env[key] = value;
  }
}

const ORG_CODE = (process.env.WMS_STAGING_ORG_CODE ?? '900152').trim();

const TENANT_DB = (
  process.env.WMS_STAGING_TENANT_DB ?? 'wms_staging_acceptance_900152'
).trim();

const RUN =
  process.env.WMS_RUN_STAGING_ACCEPTANCE === 'true' &&
  Boolean(process.env.MONGODB_URI?.trim());

function targetFromUri(uri: string): { host: string; database: string } {
  const parsed = new URL(uri);

  if (parsed.protocol !== 'mongodb:' && parsed.protocol !== 'mongodb+srv:') {
    throw new Error('Unexpected MongoDB connection-string scheme');
  }

  // Never log the URL: it may contain a username and password.
  return {
    host: parsed.host.toLowerCase(),
    database: decodeURIComponent(parsed.pathname.replace(/^\//, '')),
  };
}

(RUN ? describe : describe.skip)(
  'Phase 03A.2 staging acceptance (isolated tenant)',
  () => {
    let tenantUri: string;
    let moduleRef: TestingModule | undefined;
    let tenantMongo!: TenantMongoService;
    let collections: WmsTenantCollectionsService;
    let stagingGate: WmsStagingGateService;
    let receiving: WmsReceivingService;

    let whId: string;
    let ownerA: string;
    let ownerB: string;
    let recvLoc: string;
    let storeLoc: string;
    let skuId: string;
    let operatorAEmail: string;
    let operatorBEmail: string;
    let runSuffix: string;
    let evidence: Record<string, unknown>;

    const operatorACtx = (): WmsRequestContext => ({
      orgCode: ORG_CODE,
      organizationName: 'WMS Staging Acceptance',
      tenantUri,
      email: operatorAEmail,
      role: 'Employee',
      isWmsAdmin: false,
      allowedWarehouseIds: [whId],
      allowedOwnerIds: [ownerA],
      wmsEnabled: true,
      maxWmsWarehouses: 10,
      maxStockOwners: 50,
      operatorPermissions: {
        receiving: true,
        qc: true,
        putaway: true,
        supervise: false,
      },
    });

    const operatorBCtx = (): WmsRequestContext => ({
      ...operatorACtx(),
      email: operatorBEmail,
      allowedOwnerIds: [ownerB],
    });

    const adminCtx = (): WmsRequestContext => ({
      orgCode: ORG_CODE,
      organizationName: 'WMS Staging Acceptance',
      tenantUri,
      email: 'wms-admin-staging@partfinder.acceptance',
      role: 'Admin',
      isWmsAdmin: true,
      allowedWarehouseIds: 'all',
      allowedOwnerIds: 'all',
      wmsEnabled: true,
      maxWmsWarehouses: 10,
      maxStockOwners: 50,
      operatorPermissions: {
        receiving: true,
        qc: true,
        putaway: true,
        supervise: true,
      },
    });

    beforeAll(async () => {
      // A named-tenant DB name alone does not ensure database isolation.
      // Verify the existing platform organization first, without upserting it.
      if (ORG_CODE !== '900152' || TENANT_DB !== 'wms_staging_acceptance_900152') {
        throw new Error('Refusing to run against an unexpected organization/database');
      }

      if (process.env.WMS_DEPLOYMENT_TIER !== 'staging') {
        throw new Error('WMS_DEPLOYMENT_TIER must be staging');
      }

      if (process.env.WMS_LIVE_STOCK_POSTING === 'true') {
        throw new Error('WMS_LIVE_STOCK_POSTING must not be true');
      }

      if (process.env.WMS_STAGING_STOCK_POSTING !== 'true') {
        throw new Error('Staging stock posting must be explicitly enabled for this test');
      }

      if (process.env.WMS_ACCEPTANCE_ALLOW_WRITES !== 'YES_900152') {
        throw new Error(
          'Explicit write opt-in required: WMS_ACCEPTANCE_ALLOW_WRITES=YES_900152',
        );
      }

      const platformUri = process.env.MONGODB_URI?.trim();

      if (!platformUri) {
        throw new Error('MONGODB_URI is missing');
      }

      const platformTarget = targetFromUri(platformUri);

      if (platformTarget.database === TENANT_DB) {
        throw new Error('Platform URI must not point to the staging tenant database');
      }

      const client = new MongoClient(platformUri, {
        serverSelectionTimeoutMS: 15000,
      });

      try {
        await client.connect();

        const org = await client
          .db()
          .collection('organizations')
          .findOne({ orgCode: ORG_CODE });

        if (!org || org.wmsEnabled !== true || org.status !== 'Active') {
          throw new Error('Expected active WMS staging organization was not found');
        }

        if (org.name !== 'WMS Staging Acceptance') {
          throw new Error('Unexpected staging organization name');
        }

        if (typeof org.orgDatabaseUri !== 'string' || !org.orgDatabaseUri.trim()) {
          throw new Error('The staging organization has no tenant URI');
        }

        // Use the tenant URI stored in the verified organization record.
        tenantUri = org.orgDatabaseUri.trim();

        const tenantTarget = targetFromUri(tenantUri);

        if (tenantTarget.database !== TENANT_DB) {
          throw new Error('Organization points to an unexpected tenant database');
        }

        if (tenantTarget.host !== platformTarget.host) {
          throw new Error(
            'Tenant URI host differs from the expected platform Atlas host',
          );
        }
      } finally {
        await client.close();
      }

      // The entire acceptance workflow uses fresh identifiers per run.
      // No bulk deletes, shared warehouse resets, or platform-org updates.
      runSuffix = new Types.ObjectId().toHexString().slice(-12);

      const warehouseCode = `WH-03A2-${runSuffix}`;

      skuId = `01HWMS${runSuffix}`;

      operatorAEmail = `operator-a-03a2-${runSuffix}@partfinder.acceptance`;
      operatorBEmail = `operator-b-03a2-${runSuffix}@partfinder.acceptance`;

      moduleRef = await Test.createTestingModule({
        providers: [
          TenantMongoService,
          WmsTenantCollectionsService,
          WmsDiagnosticsService,
          WmsTransactionGuardService,
          WmsInventoryPostingService,
          WmsStagingGateService,
          WmsReceivingService,
        ],
      }).compile();

      tenantMongo = moduleRef.get(TenantMongoService);
      collections = moduleRef.get(WmsTenantCollectionsService);
      stagingGate = moduleRef.get(WmsStagingGateService);
      receiving = moduleRef.get(WmsReceivingService);

      await tenantMongo.pingUri(tenantUri);
      await tenantMongo.initializeTenantDatabase(tenantUri);

      const whColl = await collections.warehouses(tenantUri);
      const ownerColl = await collections.stockOwners(tenantUri);
      const locColl = await collections.locations(tenantUri);
      const skuColl = await collections.skus(tenantUri);
      const connection = await collections.connection(tenantUri);

      if (!connection.db) {
        throw new Error('Tenant Mongo connection has no database handle');
      }

      const grantsColl = connection.db.collection(WMS_COLLECTIONS.operatorGrants);

      whId = new Types.ObjectId().toHexString();

      await whColl.insertOne({
        _id: new Types.ObjectId(whId),
        code: warehouseCode,
        name: `Staging Acceptance ${runSuffix}`,
        isActive: true,
      });

      ownerA = new Types.ObjectId().toHexString();
      ownerB = new Types.ObjectId().toHexString();

      await ownerColl.insertOne({
        _id: new Types.ObjectId(ownerA),
        code: `CLIENT_A_${runSuffix}`,
        name: 'Client A (acceptance)',
        isActive: true,
      });

      await ownerColl.insertOne({
        _id: new Types.ObjectId(ownerB),
        code: `CLIENT_B_${runSuffix}`,
        name: 'Client B (acceptance)',
        isActive: true,
      });

      recvLoc = new Types.ObjectId().toHexString();
      storeLoc = new Types.ObjectId().toHexString();

      await locColl.insertOne({
        _id: new Types.ObjectId(recvLoc),
        warehouseId: whId,
        path: 'RECV-01',
        type: 'receiving',
        isActive: true,
      });

      await locColl.insertOne({
        _id: new Types.ObjectId(storeLoc),
        warehouseId: whId,
        path: 'A-01-01',
        type: 'storage',
        isActive: true,
      });

      await skuColl.insertOne({
        skuId,
        displayName: 'Filter',
        primaryUom: 'EA',
        status: 'active',
      });

      const permissions = {
        receiving: true,
        qc: true,
        putaway: true,
        supervise: false,
      };

      await grantsColl.insertOne({
        email: operatorAEmail,
        warehouseIds: [whId],
        ownerIds: [ownerA],
        permissions,
      });

      await grantsColl.insertOne({
        email: operatorBEmail,
        warehouseIds: [whId],
        ownerIds: [ownerB],
        permissions,
      });

      evidence = {
        phase: '03A.2',
        executedAt: new Date().toISOString(),
        orgCode: ORG_CODE,
        tenantDatabase: TENANT_DB,
        warehouseCode,
        runSuffix,
        winuiManual: 'NOT_EXECUTED',
        hostedHttpPreflight: 'NOT_EXECUTED',
        cleanup: 'NOT_PERFORMED_TEST_RECORDS_RETAINED',
      };
    }, 300_000);

    afterAll(async () => {
      try {
        await tenantMongo?.onModuleDestroy();
      } finally {
        await moduleRef?.close();
      }
    });

    it('records preflight and completes 100-filter workflow', async () => {
      const preflightDoc = await stagingGate.recordPreflightEvidence(
        adminCtx(),
        `03A.2 acceptance run ${runSuffix}`,
      );

      evidence.preflight = {
        orgCode: preflightDoc.orgCode,
        tenantDatabase: preflightDoc.tenantDatabase,
        replicaSetName: preflightDoc.replicaSetName,
        supported: preflightDoc.supported,
        commitProbe: preflightDoc.commitProbe,
        rollbackProbe: preflightDoc.rollbackProbe,
        deploymentTier: (preflightDoc as { deploymentTier?: string }).deploymentTier,
        stagingStockPosting: preflightDoc.stagingStockPosting,
        liveStockPosting: preflightDoc.liveStockPosting,
        postingGateOpen: preflightDoc.postingGateOpen,
      };

      expect(preflightDoc.supported).toBe(true);
      expect(preflightDoc.commitProbe).toBe(true);
      expect(preflightDoc.rollbackProbe).toBe(true);

      const expected = await receiving.createExpectedReceipt(operatorACtx(), {
        ownerId: ownerA,
        warehouseId: whId,
        externalPoId: `PO-100-FILTERS-${runSuffix}`,
      });

      await receiving.addExpectedLines(operatorACtx(), expected.expectedReceiptId, {
        lines: [{ skuId, qtyExpected: 100, primaryUom: 'EA' }],
      });

      const idem = `03a2-staging-${ORG_CODE}-${runSuffix}`;

      const first = await receiving.confirmGoodsReceipt(operatorACtx(), {
        idempotencyKey: idem,
        ownerId: ownerA,
        warehouseId: whId,
        receivingLocationId: recvLoc,
        expectedReceiptId: expected.expectedReceiptId,
        lines: [{ skuId, qtyReceived: 60 }],
      });

      expect(first.duplicate).toBe(false);

      const dup = await receiving.confirmGoodsReceipt(operatorACtx(), {
        idempotencyKey: idem,
        ownerId: ownerA,
        warehouseId: whId,
        receivingLocationId: recvLoc,
        expectedReceiptId: expected.expectedReceiptId,
        lines: [{ skuId, qtyReceived: 60 }],
      });

      expect(dup.duplicate).toBe(true);

      const lines = await receiving.listReceiptLines(
        operatorACtx(),
        first.goodsReceiptId,
      );

      const lineId = String(
        (lines.items[0] as { receiptLineId: string }).receiptLineId,
      );

      await receiving.applyQc(operatorACtx(), lineId, {
        status: 'partial',
        qtyAccepted: 55,
        qtyRejected: 5,
        note: `03A.2 acceptance run ${runSuffix}`,
      });

      const putaways = await receiving.listPutaway(operatorACtx(), whId);

      const taskId = String(
        (putaways.items[0] as { putawayTaskId: string }).putawayTaskId,
      );

      await receiving.completePutaway(operatorACtx(), taskId, {
        idempotencyKey: `put-${idem}`,
        toLocationId: storeLoc,
      });

      const balColl = await collections.stockBalances(tenantUri);

      const recvBal = await balColl.findOne({
        skuKey: skuId,
        locationId: recvLoc,
      });

      const storeBal = await balColl.findOne({
        skuKey: skuId,
        locationId: storeLoc,
      });

      const onHandRecv = Number(recvBal?.onHand ?? 0);
      const blockedRecv = Number(recvBal?.blockedQty ?? 0);
      const onHandStore = Number(storeBal?.onHand ?? 0);

      const expectedDoc = await (
        await collections.expectedReceipts(tenantUri)
      ).findOne({
        expectedReceiptId: expected.expectedReceiptId,
      });

      const row = (
        expectedDoc?.lines as Array<Record<string, unknown>> | undefined
      )?.[0];

      let crossOwnerBlocked = false;

      try {
        await receiving.createExpectedReceipt(operatorBCtx(), {
          ownerId: ownerA,
          warehouseId: whId,
        });
      } catch (error) {
        crossOwnerBlocked = error instanceof ForbiddenException;
      }

      expect(crossOwnerBlocked).toBe(true);

      evidence.scenario = {
        idempotencyKey: idem,
        duplicateSecondPost: dup.duplicate,
        onHandWarehouseTotal: onHandRecv + onHandStore,
        onHandReceiving: onHandRecv,
        onHandStorage: onHandStore,
        blockedAtReceiving: blockedRecv,
        qtyExpectedRemaining: 100 - Number(row?.qtyReceivedTotal ?? 0),
        expectedStatus: expectedDoc?.status,
        crossOwner403: crossOwnerBlocked,
      };

      expect(onHandRecv + onHandStore).toBe(60);
      expect(blockedRecv).toBe(5);
      expect(onHandStore).toBe(55);
      expect(Number(row?.qtyReceivedTotal)).toBe(60);
      expect(String(expectedDoc?.status)).toBe('partially_received');
      expect(100 - Number(row?.qtyReceivedTotal ?? 0)).toBe(40);

      const outDir = join(
        __dirname,
        '..',
        '..',
        '..',
        'docs',
        'wms-phase03',
        'evidence',
      );

      mkdirSync(outDir, { recursive: true });

      writeFileSync(
        join(outDir, '03a2-staging-evidence.json'),
        JSON.stringify(evidence, null, 2),
        'utf8',
      );
    }, 300_000);
  },
);
