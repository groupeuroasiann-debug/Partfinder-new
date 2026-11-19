
import { BadRequestException } from '@nestjs/common';
import { WmsSkuService } from './wms-sku.service';
import type { WmsRequestContext } from '../wms.types';

describe('WmsSkuService merge guards', () => {
  // Mock WMS administrator context
  const ctx: WmsRequestContext = {
    orgCode: 't',
    organizationName: 'T',
    tenantUri: 'mongodb://x',
    email: 'a@t.com',
    role: 'Admin',
    isWmsAdmin: true,
    allowedWarehouseIds: 'all',
    allowedOwnerIds: 'all',
    wmsEnabled: true,
    maxWmsWarehouses: 10,
    maxStockOwners: 10,

    // Required WMS operator permissions
    operatorPermissions: {
      receiving: true,
      qc: true,
      putaway: true,
      supervise: true,
    },
  };

  function makeService(hasStock: boolean, txnRefs: number) {
    // Mock database collections
    const collections = {
      ensurePhase02Indexes: jest.fn(),
      skus: jest.fn(),
      skuIdentifiers: jest.fn(),
    };

    // Mock audit service
    const audit = {
      append: jest.fn(),
    };

    // Mock unit-of-measure service
    const uom = {
      assertUomExists: jest.fn(),
    };

    // Mock inventory reading service
    const inventoryRead = {
      hasStockForSku: jest.fn().mockResolvedValue(hasStock),
      countTxnReferences: jest.fn().mockResolvedValue(txnRefs),
    };

    // Create WMS SKU service
    const svc = new WmsSkuService(
      collections as never,
      audit as never,
      uom as never,
      inventoryRead as never,
    );

    // Mock findSku with correctly typed parameters.
    // Spy on this instance to avoid affecting other tests.
    const skuServiceWithFind = svc as unknown as {
      findSku: (
        context: WmsRequestContext,
        id: string,
      ) => Promise<unknown>;
    };

    jest
      .spyOn(skuServiceWithFind, 'findSku')
      .mockImplementation(
        async (_context: WmsRequestContext, id: string) => ({
          skuId: id,
          status: 'active',
          variantGroupId: 'g1',
        }),
      );

    return {
      svc,
      collections,
    };
  }

  it('rejects merge when source SKU has stock', async () => {
    const { svc } = makeService(true, 0);

    await expect(
      svc.mergeSku(ctx, 'SRC', {
        mergedIntoSkuId: 'TGT',
        reason: 'test',
      }),
    ).rejects.toBeInstanceOf(BadRequestException);
  });

  it('requires merge reason when ledger references exist', async () => {
    const { svc } = makeService(false, 2);

    await expect(
      svc.mergeSku(ctx, 'SRC', {
        mergedIntoSkuId: 'TGT',
      }),
    ).rejects.toBeInstanceOf(BadRequestException);
  });
});
