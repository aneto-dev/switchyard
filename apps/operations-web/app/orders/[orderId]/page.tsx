import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { Field, Identifier } from "@/app/components/detail-fields";
import { StateBadge } from "@/app/components/state-badge";
import { formatDateTime, formatMoney, humanise } from "@/lib/format";
import { getOrder, getOrderFulfilment } from "@/lib/operations-api";
import { FulfilmentSection } from "./fulfilment-section";

type Params = Promise<{ orderId: string }>;

export const metadata: Metadata = { title: "Order" };

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export default async function OrderPage({ params }: { params: Params }) {
  const { orderId } = await params;

  if (!guidPattern.test(orderId)) {
    notFound();
  }

  // Each context answers from its own data; fetching in parallel keeps the page to one round trip.
  const [order, fulfilment] = await Promise.all([getOrder(orderId), getOrderFulfilment(orderId)]);

  if (!order) {
    notFound();
  }

  return (
    <div className="space-y-8">
      <div>
        <Link href="/orders" className="text-sm text-[var(--muted)] underline-offset-4 hover:underline">
          ← Orders
        </Link>
        <div className="mt-3 flex flex-wrap items-center gap-3">
          <h1 className="font-mono text-2xl font-semibold tracking-tight">{order.orderNumber}</h1>
          <StateBadge state={order.status} />
        </div>
      </div>

      <section className="rounded-lg border border-[var(--border)] p-5">
        <dl className="grid gap-5 sm:grid-cols-2 lg:grid-cols-4">
          <Field label="Total">{formatMoney(order.totalAmount, order.currency)}</Field>
          <Field label="Placed">{formatDateTime(order.createdAtUtc)}</Field>
          <Field label="Order ID">
            <Identifier value={order.orderId} />
          </Field>
        </dl>
      </section>

      <section className="space-y-3">
        <h2 className="text-lg font-semibold">Placement</h2>
        {order.placement ? (
          <dl className="grid gap-5 rounded-lg border border-[var(--border)] p-5 sm:grid-cols-2 lg:grid-cols-4">
            <Field label="Stage">
              <StateBadge state={order.placement.state} />
            </Field>
            <Field label="Started">{formatDateTime(order.placement.startedAtUtc)}</Field>
            <Field label="Last updated">{formatDateTime(order.placement.updatedAtUtc)}</Field>
            <Field label="Payment authorisation request">
              <Identifier value={order.placement.paymentAuthorisationRequestId} />
            </Field>
          </dl>
        ) : (
          <p className="rounded-lg border border-[var(--border)] px-5 py-4 text-sm text-[var(--muted)]">
            This order was accepted before the placement workflow existed, so it has no placement record.
          </p>
        )}
      </section>

      <section className="space-y-3">
        <h2 className="text-lg font-semibold">Payment authorisation</h2>
        {order.payment ? (
          <dl className="grid gap-5 rounded-lg border border-[var(--border)] p-5 sm:grid-cols-2 lg:grid-cols-4">
            <Field label="Outcome">
              <StateBadge state={order.payment.outcome} />
            </Field>
            <Field label="Reported by Payments">{formatDateTime(order.payment.outcomeAtUtc)}</Field>
            <Field label="Amount">{formatMoney(order.payment.amount, order.payment.currency)}</Field>
            <Field label="Reason">{humanise(order.payment.reason)}</Field>
            <Field label="Payment ID">
              <Identifier value={order.payment.paymentId} />
            </Field>
            <Field label="Provider reference">
              <Identifier value={order.payment.providerReference} />
            </Field>
            <Field label="Reconciliation attempts">{order.payment.reconciliationAttemptCount ?? "—"}</Field>
            <Field label="Recorded by Ordering">{formatDateTime(order.payment.recordedAtUtc)}</Field>
          </dl>
        ) : (
          <p className="rounded-lg border border-[var(--border)] px-5 py-4 text-sm text-[var(--muted)]">
            {order.placement?.paymentAuthorisationRequestId
              ? "No authorisation outcome recorded. Payments has not reported one yet, or it was reported before Ordering started recording outcomes."
              : "Payment authorisation has not been requested."}
          </p>
        )}
      </section>

      <section className="space-y-3">
        <h2 className="text-lg font-semibold">Lines and reservations</h2>
        <div className="overflow-x-auto rounded-lg border border-[var(--border)]">
          <table className="w-full min-w-[60rem] text-left text-sm">
            <thead className="bg-white/5 text-xs uppercase tracking-wide text-[var(--muted)]">
              <tr>
                <th className="px-4 py-3 font-medium">Product</th>
                <th className="px-4 py-3 text-right font-medium">Qty</th>
                <th className="px-4 py-3 text-right font-medium">Unit price</th>
                <th className="px-4 py-3 text-right font-medium">Line total</th>
                <th className="px-4 py-3 font-medium">Reservation</th>
                <th className="px-4 py-3 font-medium">Reservation ID</th>
                <th className="px-4 py-3 font-medium">Expires</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-[var(--border)]">
              {order.lines.map((line) => (
                <tr key={line.orderLineId}>
                  <td className="px-4 py-3">
                    <div>{line.productName}</div>
                    <div className="font-mono text-xs text-[var(--muted)]">{line.skuCode}</div>
                  </td>
                  <td className="px-4 py-3 text-right tabular-nums">{line.quantity}</td>
                  <td className="px-4 py-3 text-right tabular-nums">
                    {formatMoney(line.unitPriceAmount, line.currency)}
                  </td>
                  <td className="px-4 py-3 text-right tabular-nums">
                    {formatMoney(line.lineTotalAmount, line.currency)}
                  </td>
                  <td className="px-4 py-3">
                    <StateBadge state={line.reservationState} />
                  </td>
                  <td className="px-4 py-3">
                    <Identifier value={line.reservationId} />
                  </td>
                  <td className="px-4 py-3 text-[var(--muted)]">{formatDateTime(line.reservationExpiresAtUtc)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <FulfilmentSection fulfilment={fulfilment} />

      <p className="text-xs text-[var(--muted)]">Return and refund state are not shown here yet.</p>
    </div>
  );
}
