import type { Metadata } from "next";
import Link from "next/link";
import { StateBadge } from "@/app/components/state-badge";
import { formatDateTime, formatMoney } from "@/lib/format";
import {
  listOrders,
  OperationsApiError,
  orderStatuses,
  type OrderStatus,
  type OrderSummary,
} from "@/lib/operations-api";

export const metadata: Metadata = { title: "Orders" };

type SearchParams = Promise<Record<string, string | string[] | undefined>>;

function single(value: string | string[] | undefined): string | undefined {
  const first = Array.isArray(value) ? value[0] : value;
  const trimmed = first?.trim();

  return trimmed ? trimmed : undefined;
}

function parseStatus(value: string | undefined): OrderStatus | undefined {
  return orderStatuses.find((status) => status === value);
}

export default async function OrdersPage({ searchParams }: { searchParams: SearchParams }) {
  const params = await searchParams;
  const status = parseStatus(single(params.status));
  const orderNumber = single(params.orderNumber);

  let orders: OrderSummary[] = [];
  let error: string | null = null;

  try {
    orders = await listOrders({ status, orderNumber });
  } catch (exception) {
    if (!(exception instanceof OperationsApiError)) {
      throw exception;
    }

    error = exception.message;
  }

  const filtered = Boolean(status || orderNumber);

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Orders</h1>
        <p className="mt-1 text-sm text-[var(--muted)]">
          Most recent orders first, with where each one is in the placement workflow.
        </p>
      </div>

      <form className="flex flex-wrap items-end gap-3" action="/orders">
        <label className="flex flex-col gap-1 text-xs text-[var(--muted)]">
          Order number
          <input
            name="orderNumber"
            defaultValue={orderNumber}
            placeholder="SW-00100000"
            maxLength={32}
            className="w-48 rounded-md border border-[var(--border)] bg-black/20 px-3 py-2 text-sm text-[var(--text)] placeholder:text-slate-500"
          />
        </label>
        <label className="flex flex-col gap-1 text-xs text-[var(--muted)]">
          Status
          <select
            name="status"
            defaultValue={status ?? ""}
            className="w-40 rounded-md border border-[var(--border)] bg-black/20 px-3 py-2 text-sm text-[var(--text)]"
          >
            <option value="">All statuses</option>
            {orderStatuses.map((option) => (
              <option key={option} value={option}>
                {option}
              </option>
            ))}
          </select>
        </label>
        <button
          type="submit"
          className="rounded-md bg-sky-400/90 px-4 py-2 text-sm font-medium text-slate-950 hover:bg-sky-300"
        >
          Apply
        </button>
        {filtered ? (
          <Link href="/orders" className="py-2 text-sm text-[var(--muted)] underline-offset-4 hover:underline">
            Clear
          </Link>
        ) : null}
      </form>

      {error ? (
        <p role="alert" className="rounded-md border border-rose-400/40 bg-rose-400/10 px-4 py-3 text-sm text-rose-100">
          {error}
        </p>
      ) : orders.length === 0 ? (
        <p className="rounded-md border border-[var(--border)] px-4 py-6 text-sm text-[var(--muted)]">
          {filtered ? "No orders match these filters." : "No orders have been placed yet."}
        </p>
      ) : (
        <div className="overflow-x-auto rounded-lg border border-[var(--border)]">
          <table className="w-full min-w-[56rem] text-left text-sm">
            <thead className="bg-white/5 text-xs uppercase tracking-wide text-[var(--muted)]">
              <tr>
                <th className="px-4 py-3 font-medium">Order</th>
                <th className="px-4 py-3 font-medium">Status</th>
                <th className="px-4 py-3 font-medium">Placement</th>
                <th className="px-4 py-3 text-right font-medium">Lines</th>
                <th className="px-4 py-3 text-right font-medium">Total</th>
                <th className="px-4 py-3 font-medium">Placed</th>
                <th className="px-4 py-3 font-medium">Placement updated</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-[var(--border)]">
              {orders.map((order) => (
                <tr key={order.orderId} className="hover:bg-white/[0.03]">
                  <td className="whitespace-nowrap px-4 py-3 font-mono">
                    <Link href={`/orders/${order.orderId}`} className="text-[var(--accent)] underline-offset-4 hover:underline">
                      {order.orderNumber}
                    </Link>
                  </td>
                  <td className="px-4 py-3">
                    <StateBadge state={order.status} />
                  </td>
                  <td className="px-4 py-3">
                    <StateBadge state={order.placementState} />
                  </td>
                  <td className="px-4 py-3 text-right tabular-nums">{order.lineCount}</td>
                  <td className="px-4 py-3 text-right tabular-nums">
                    {formatMoney(order.totalAmount, order.currency)}
                  </td>
                  <td className="px-4 py-3 text-[var(--muted)]">{formatDateTime(order.createdAtUtc)}</td>
                  <td className="px-4 py-3 text-[var(--muted)]">{formatDateTime(order.placementUpdatedAtUtc)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
