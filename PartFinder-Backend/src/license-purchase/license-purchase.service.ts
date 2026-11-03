import {
  BadRequestException,
  Injectable,
  InternalServerErrorException,
  ServiceUnavailableException,
} from '@nestjs/common';
import { ConfigService } from '@nestjs/config';
import { InjectModel } from '@nestjs/mongoose';
import { createHmac, timingSafeEqual } from 'crypto';
import { Model } from 'mongoose';
import { OrganizationsService } from '../organizations/organizations.service';
import { StartLicenseCheckoutDto } from './dto/start-license-checkout.dto';
import { VerifyLicensePaymentDto } from './dto/verify-license-payment.dto';
import {
  LicensePurchase,
  LicensePurchaseDocument,
} from './schemas/license-purchase.schema';

/** PartFinder self-serve licence price (USD). */
export const LICENSE_PRICE_USD_CENTS = 1000;

@Injectable()
export class LicensePurchaseService {
  constructor(
    @InjectModel(LicensePurchase.name)
    private readonly purchaseModel: Model<LicensePurchaseDocument>,
    private readonly organizations: OrganizationsService,
    private readonly config: ConfigService,
  ) {}

  async startCheckout(dto: StartLicenseCheckoutDto) {
    const { keyId, keySecret } = this.razorpayKeys();
    const organisationName = dto.organisationName.trim();
    const email = dto.email.trim().toLowerCase();
    const mobile = dto.mobile.trim();

    const receipt = `pf-lic-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
    const order = await this.createRazorpayOrder({
      keyId,
      keySecret,
      amountCents: LICENSE_PRICE_USD_CENTS,
      currency: 'USD',
      receipt,
      notes: {
        organisationName,
        email,
        mobile,
      },
    });

    const orderId = String(order.id);
    await this.purchaseModel.create({
      organisationName,
      email,
      mobile,
      razorpayOrderId: orderId,
      razorpayPaymentId: null,
      status: 'pending',
      orgCode: null,
      temporaryPassword: null,
      amountCents: LICENSE_PRICE_USD_CENTS,
      currency: 'USD',
    });

    return {
      ok: true as const,
      keyId,
      orderId,
      amount: LICENSE_PRICE_USD_CENTS,
      currency: 'USD',
      description: 'PartFinder organisation licence',
      prefill: {
        name: organisationName,
        email,
        contact: mobile.replace(/\D/g, ''),
      },
    };
  }

  async verifyPayment(dto: VerifyLicensePaymentDto) {
    const { keySecret } = this.razorpayKeys();
    const orderId = dto.razorpayOrderId.trim();
    const paymentId = dto.razorpayPaymentId.trim();
    const signature = dto.razorpaySignature.trim();

    if (!this.verifyCheckoutSignature(orderId, paymentId, signature, keySecret)) {
      throw new BadRequestException('Payment verification failed.');
    }

    const purchase = await this.purchaseModel.findOne({ razorpayOrderId: orderId }).exec();
    if (!purchase) {
      throw new BadRequestException('Unknown payment order.');
    }

    if (purchase.status === 'fulfilled' && purchase.orgCode && purchase.temporaryPassword) {
      return {
        ok: true as const,
        organisationName: purchase.organisationName,
        email: purchase.email,
        orgCode: purchase.orgCode,
        temporaryPassword: purchase.temporaryPassword,
      };
    }

    await this.assertPaymentCaptured(paymentId, orderId);

    if (purchase.status === 'pending') {
      purchase.status = 'paid';
      purchase.razorpayPaymentId = paymentId;
      await purchase.save();
    }

    if (!purchase.orgCode || !purchase.temporaryPassword) {
      const provisioned = await this.organizations.createSelfServeLicense({
        name: purchase.organisationName,
        firstAdminEmail: purchase.email,
        plan: 'starter',
        type: 'standard',
      });
      purchase.status = 'fulfilled';
      purchase.orgCode = provisioned.orgCode;
      purchase.temporaryPassword = provisioned.temporaryPassword;
      await purchase.save();
    }

    return {
      ok: true as const,
      organisationName: purchase.organisationName,
      email: purchase.email,
      orgCode: purchase.orgCode!,
      temporaryPassword: purchase.temporaryPassword!,
    };
  }

  private razorpayKeys() {
    const keyId = this.config.get<string>('RAZORPAY_KEY_ID')?.trim() ?? '';
    const keySecret = this.config.get<string>('RAZORPAY_KEY_SECRET')?.trim() ?? '';
    if (!keyId || !keySecret) {
      throw new ServiceUnavailableException(
        'Online licence payment is not configured. Contact support.',
      );
    }
    return { keyId, keySecret };
  }

  private verifyCheckoutSignature(
    orderId: string,
    paymentId: string,
    signature: string,
    keySecret: string,
  ) {
    const expected = createHmac('sha256', keySecret)
      .update(`${orderId}|${paymentId}`)
      .digest('hex');
    try {
      const a = Buffer.from(expected, 'utf8');
      const b = Buffer.from(signature, 'utf8');
      return a.length === b.length && timingSafeEqual(a, b);
    } catch {
      return false;
    }
  }

  private async createRazorpayOrder(input: {
    keyId: string;
    keySecret: string;
    amountCents: number;
    currency: string;
    receipt: string;
    notes: Record<string, string>;
  }) {
    const auth = Buffer.from(`${input.keyId}:${input.keySecret}`).toString('base64');
    const response = await fetch('https://api.razorpay.com/v1/orders', {
      method: 'POST',
      headers: {
        Authorization: `Basic ${auth}`,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        amount: input.amountCents,
        currency: input.currency,
        receipt: input.receipt,
        notes: input.notes,
      }),
    });
    const body: unknown = await response.json().catch(() => null);
    if (!response.ok) {
      const message =
        body && typeof body === 'object' && 'error' in body
          ? String((body as { error?: { description?: string } }).error?.description ?? '')
          : '';
      throw new BadRequestException(
        message || 'Could not start payment. Check Razorpay currency settings (USD).',
      );
    }
    const order = body as { id?: string };
    if (!order?.id) {
      throw new InternalServerErrorException('Invalid response from payment provider.');
    }
    return order as { id: string };
  }

  private async assertPaymentCaptured(paymentId: string, orderId: string) {
    const { keyId, keySecret } = this.razorpayKeys();
    const auth = Buffer.from(`${keyId}:${keySecret}`).toString('base64');
    const response = await fetch(`https://api.razorpay.com/v1/payments/${paymentId}`, {
      headers: { Authorization: `Basic ${auth}` },
    });
    const body: unknown = await response.json().catch(() => null);
    if (!response.ok) {
      throw new BadRequestException('Could not confirm payment status.');
    }
    const payment = body as {
      status?: string;
      order_id?: string;
      amount?: number;
      currency?: string;
    };
    if (payment.status !== 'captured' && payment.status !== 'authorized') {
      throw new BadRequestException('Payment was not completed.');
    }
    if (payment.order_id !== orderId) {
      throw new BadRequestException('Payment does not match this order.');
    }
    if (
      payment.amount !== LICENSE_PRICE_USD_CENTS ||
      (payment.currency ?? '').toUpperCase() !== 'USD'
    ) {
      throw new BadRequestException('Payment amount does not match the licence price.');
    }
  }
}
