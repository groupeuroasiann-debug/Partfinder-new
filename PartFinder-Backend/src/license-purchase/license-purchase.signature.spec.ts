import { createHmac } from 'crypto';
import { verifyRazorpayCheckoutSignature } from './license-purchase.signature';

describe('verifyRazorpayCheckoutSignature', () => {
  const secret = 'test_secret_only';
  const orderId = 'order_test123';
  const paymentId = 'pay_test456';

  function sign(order: string, payment: string) {
    return createHmac('sha256', secret).update(`${order}|${payment}`).digest('hex');
  }

  it('accepts a valid signature', () => {
    const signature = sign(orderId, paymentId);
    expect(verifyRazorpayCheckoutSignature(orderId, paymentId, signature, secret)).toBe(
      true,
    );
  });

  it('rejects tampered payment id', () => {
    const signature = sign(orderId, paymentId);
    expect(
      verifyRazorpayCheckoutSignature(orderId, 'pay_other', signature, secret),
    ).toBe(false);
  });

  it('rejects wrong secret', () => {
    const signature = sign(orderId, paymentId);
    expect(
      verifyRazorpayCheckoutSignature(orderId, paymentId, signature, 'wrong_secret'),
    ).toBe(false);
  });
});
