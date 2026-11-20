import { Identifier } from "@/app/components/detail-fields";
import { StateBadge } from "@/app/components/state-badge";
import { formatDateTime, formatMoney } from "@/lib/format";
import type { OrderLine, OrderReturns } from "@/lib/operations-api";

export function ReturnsSection({ returns, orderLines }: { returns: OrderReturns | null; orderLines: OrderLine[] }) {
  if (!returns) {
    return (
      <section className="space-y-3">
        <h2 className="text-lg font-semibold">Returns and refunds</h2>
        <p className="rounded-lg border border-[var(--border)] px-5 py-4 text-sm text-[var(--muted)]">
          Nothing to return yet. Returns opens once a shipment for this order is delivered.
        </p>
      </section>
    );
  }

  // Returns stores order line IDs only; names come from the Ordering order lines on this page.
  const linesById = new Map(orderLines.map((line) => [line.orderLineId, line]));
  const lines = [...returns.lines].sort(
    (left, right) =>
      orderLines.findIndex((line) => line.orderLineId === left.orderLineId) -
      orderLines.findIndex((line) => line.orderLineId === right.orderLineId),
  );

  return (
    <section className="space-y-4">
      <h2 className="text-lg font-semibold">Returns and refunds</h2>

      <div className="overflow-x-auto rounded-lg border border-[var(--border)]">
        <table className="w-full min-w-[36rem] text-left text-sm">
          <thead className="bg-white/5 text-xs uppercase tracking-wide text-[var(--muted)]">
            <tr>
              <th className="px-4 py-3 font-medium">Product</th>
              <th className="px-4 py-3 text-right font-medium">Delivered</th>
              <th className="px-4 py-3 text-right font-medium">Awaiting return</th>
              <th className="px-4 py-3 text-right font-medium">Returned</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-[var(--border)]">
            {lines.map((line) => {
              const orderLine = linesById.get(line.orderLineId);

              return (
                <tr key={line.orderLineId}>
                  <td className="px-4 py-3">
                    <div>{orderLine?.productName ?? "Unknown line"}</div>
                    <div className="font-mono text-xs text-[var(--muted)]">{orderLine?.skuCode ?? line.orderLineId}</div>
                  </td>
                  <td className="px-4 py-3 text-right tabular-nums">{line.deliveredQuantity}</td>
                  <td className="px-4 py-3 text-right tabular-nums">{line.authorisedReturnQuantity}</td>
                  <td className="px-4 py-3 text-right tabular-nums">{line.returnedQuantity}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>

      {returns.requests.length === 0 ? (
        <p className="text-sm text-[var(--muted)]">No return requests.</p>
      ) : (
        <div className="space-y-3">
          {returns.requests.map((request) => (
            <div key={request.returnRequestId} className="rounded-lg border border-[var(--border)]">
              <div className="flex flex-wrap items-center gap-3 border-b border-[var(--border)] px-4 py-3 text-sm">
                <span className="font-medium">Return request</span>
                <StateBadge state={request.status} />
                <span className="tabular-nums">
                  {request.quantity} {request.quantity === 1 ? "item" : "items"}
                </span>
                <span className="ml-auto text-[var(--muted)]">{formatDateTime(request.requestedAtUtc)}</span>
              </div>
              {request.receipts.length === 0 ? (
                <p className="px-4 py-3 text-sm text-[var(--muted)]">Nothing received yet.</p>
              ) : (
                <div className="overflow-x-auto">
                  <table className="w-full min-w-[48rem] text-left text-sm">
                    <thead className="text-xs uppercase tracking-wide text-[var(--muted)]">
                      <tr>
                        <th className="px-4 py-2 font-medium">Received</th>
                        <th className="px-4 py-2 text-right font-medium">Items</th>
                        <th className="px-4 py-2 font-medium">Refund</th>
                        <th className="px-4 py-2 text-right font-medium">Amount</th>
                        <th className="px-4 py-2 font-medium">Provider reference</th>
                        <th className="px-4 py-2 font-medium">Refund updated</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-[var(--border)]">
                      {request.receipts.map((receipt) => (
                        <tr key={receipt.returnReceiptId}>
                          <td className="px-4 py-3 text-[var(--muted)]">{formatDateTime(receipt.receivedAtUtc)}</td>
                          <td className="px-4 py-3 text-right tabular-nums">{receipt.quantity}</td>
                          <td className="px-4 py-3">
                            {receipt.refund ? (
                              <StateBadge state={receipt.refund.status} />
                            ) : (
                              <span className="text-[var(--muted)]">Not started</span>
                            )}
                          </td>
                          <td className="px-4 py-3 text-right tabular-nums">
                            {receipt.refund ? formatMoney(receipt.refund.amount, receipt.refund.currency) : "—"}
                          </td>
                          <td className="px-4 py-3">
                            <Identifier value={receipt.refund?.providerReference ?? null} />
                          </td>
                          <td className="px-4 py-3 text-[var(--muted)]">
                            {formatDateTime(receipt.refund?.updatedAtUtc ?? null)}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>
          ))}
        </div>
      )}
    </section>
  );
}
