import { Field, Identifier } from "@/app/components/detail-fields";
import { StateBadge } from "@/app/components/state-badge";
import { formatDateTime, humanise } from "@/lib/format";
import type { OrderFulfilment } from "@/lib/operations-api";

function shipmentState(shipment: OrderFulfilment["shipments"][number]): string {
  if (shipment.deliveredAtUtc) {
    return "Delivered";
  }

  return shipment.dispatchedAtUtc ? "Dispatched" : "Created";
}

export function FulfilmentSection({ fulfilment }: { fulfilment: OrderFulfilment | null }) {
  if (!fulfilment) {
    return (
      <section className="space-y-3">
        <h2 className="text-lg font-semibold">Fulfilment</h2>
        <p className="rounded-lg border border-[var(--border)] px-5 py-4 text-sm text-[var(--muted)]">
          Fulfilment has not accepted this order. It starts once the order is confirmed.
        </p>
      </section>
    );
  }

  const { capture } = fulfilment;

  return (
    <section className="space-y-4">
      <h2 className="text-lg font-semibold">Fulfilment</h2>

      <dl className="grid gap-5 rounded-lg border border-[var(--border)] p-5 sm:grid-cols-2 lg:grid-cols-4">
        <Field label="Preparation">
          <StateBadge state={fulfilment.status} />
        </Field>
        <Field label="Accepted">{formatDateTime(fulfilment.confirmedAtUtc)}</Field>
        <Field label="Payment capture">
          <StateBadge state={capture.status} />
        </Field>
        <Field label="Capture requested">{formatDateTime(capture.requestedAtUtc)}</Field>
        <Field label="Capture resolved">{formatDateTime(capture.resolvedAtUtc)}</Field>
        <Field label="Capture provider reference">
          <Identifier value={capture.providerReference} />
        </Field>
        <Field label="Capture request">
          <Identifier value={capture.requestId} />
        </Field>
      </dl>

      <div className="overflow-x-auto rounded-lg border border-[var(--border)]">
        <table className="w-full min-w-[36rem] text-left text-sm">
          <thead className="bg-white/5 text-xs uppercase tracking-wide text-[var(--muted)]">
            <tr>
              <th className="px-4 py-3 font-medium">Product</th>
              <th className="px-4 py-3 text-right font-medium">Ordered</th>
              <th className="px-4 py-3 text-right font-medium">Prepared</th>
              <th className="px-4 py-3 text-right font-medium">Cancelled</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-[var(--border)]">
            {fulfilment.lines.map((line) => (
              <tr key={line.orderLineId}>
                <td className="px-4 py-3">
                  <div>{line.productName}</div>
                  <div className="font-mono text-xs text-[var(--muted)]">{line.skuCode}</div>
                </td>
                <td className="px-4 py-3 text-right tabular-nums">{line.quantity}</td>
                <td className="px-4 py-3 text-right tabular-nums">{line.preparedQuantity}</td>
                <td className="px-4 py-3 text-right tabular-nums">{line.cancelledQuantity}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="space-y-2">
        <h3 className="text-sm font-semibold">Shipments</h3>
        {fulfilment.shipments.length === 0 ? (
          <p className="text-sm text-[var(--muted)]">No shipments yet.</p>
        ) : (
          <div className="overflow-x-auto rounded-lg border border-[var(--border)]">
            <table className="w-full min-w-[48rem] text-left text-sm">
              <thead className="bg-white/5 text-xs uppercase tracking-wide text-[var(--muted)]">
                <tr>
                  <th className="px-4 py-3 font-medium">Shipment</th>
                  <th className="px-4 py-3 font-medium">State</th>
                  <th className="px-4 py-3 text-right font-medium">Items</th>
                  <th className="px-4 py-3 font-medium">Created</th>
                  <th className="px-4 py-3 font-medium">Dispatched</th>
                  <th className="px-4 py-3 font-medium">Delivered</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-[var(--border)]">
                {fulfilment.shipments.map((shipment) => (
                  <tr key={shipment.shipmentId}>
                    <td className="px-4 py-3">
                      <Identifier value={shipment.shipmentId} />
                    </td>
                    <td className="px-4 py-3">
                      <StateBadge state={shipmentState(shipment)} />
                    </td>
                    <td className="px-4 py-3 text-right tabular-nums">{shipment.itemQuantity}</td>
                    <td className="px-4 py-3 text-[var(--muted)]">{formatDateTime(shipment.createdAtUtc)}</td>
                    <td className="px-4 py-3 text-[var(--muted)]">{formatDateTime(shipment.dispatchedAtUtc)}</td>
                    <td className="px-4 py-3 text-[var(--muted)]">{formatDateTime(shipment.deliveredAtUtc)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {fulfilment.cancellations.length > 0 ? (
        <div className="space-y-2">
          <h3 className="text-sm font-semibold">Cancellations</h3>
          <ul className="divide-y divide-[var(--border)] rounded-lg border border-[var(--border)]">
            {fulfilment.cancellations.map((cancellation) => (
              <li key={cancellation.cancellationId} className="flex flex-wrap items-center gap-3 px-4 py-3 text-sm">
                <StateBadge state={cancellation.outcome} />
                <span className="tabular-nums">
                  {cancellation.quantity} {cancellation.quantity === 1 ? "item" : "items"}
                </span>
                {cancellation.rejectionReason ? (
                  <span className="text-[var(--muted)]">{humanise(cancellation.rejectionReason)}</span>
                ) : null}
                <span className="ml-auto text-[var(--muted)]">{formatDateTime(cancellation.requestedAtUtc)}</span>
              </li>
            ))}
          </ul>
        </div>
      ) : null}
    </section>
  );
}
