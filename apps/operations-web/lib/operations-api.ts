// Server-side client for the Switchyard operations API. Only server components import this,
// so the API base URL never reaches the browser.

export const orderStatuses = ["Pending", "Confirmed", "Failed"] as const;

export type OrderStatus = (typeof orderStatuses)[number];

export type OrderSummary = {
  orderId: string;
  orderNumber: string;
  status: OrderStatus;
  placementState: string | null;
  totalAmount: number;
  currency: string;
  lineCount: number;
  createdAtUtc: string;
  placementUpdatedAtUtc: string | null;
};

export type OrderPlacement = {
  state: string;
  startedAtUtc: string;
  updatedAtUtc: string;
  paymentAuthorisationRequestId: string | null;
};

export type PaymentAuthorisationOutcome = "Indeterminate" | "NeedsAttention" | "Authorised" | "Failed";

export type OrderPayment = {
  outcome: PaymentAuthorisationOutcome;
  requestId: string;
  paymentId: string | null;
  amount: number;
  currency: string;
  providerReference: string | null;
  reason: string | null;
  reconciliationAttemptCount: number | null;
  outcomeAtUtc: string;
  recordedAtUtc: string;
};

export type OrderLine = {
  orderLineId: string;
  skuCode: string;
  productName: string;
  quantity: number;
  unitPriceAmount: number;
  lineTotalAmount: number;
  currency: string;
  reservationState: string | null;
  reservationRequestId: string | null;
  reservationId: string | null;
  reservationExpiresAtUtc: string | null;
};

export type OrderDetail = {
  orderId: string;
  orderNumber: string;
  status: OrderStatus;
  totalAmount: number;
  currency: string;
  createdAtUtc: string;
  placement: OrderPlacement | null;
  payment: OrderPayment | null;
  lines: OrderLine[];
};

export type OrderListFilter = {
  status?: OrderStatus;
  orderNumber?: string;
};

export class OperationsApiError extends Error {}

const apiBaseUrl = process.env.SWITCHYARD_API_URL ?? "http://localhost:5000";

async function getJson<T>(path: string): Promise<T | null> {
  let response: Response;

  try {
    response = await fetch(new URL(path, apiBaseUrl), { cache: "no-store" });
  } catch {
    throw new OperationsApiError(`Could not reach the Switchyard API at ${apiBaseUrl}.`);
  }

  if (response.status === 404) {
    return null;
  }

  if (!response.ok) {
    throw new OperationsApiError(`The Switchyard API returned ${response.status}.`);
  }

  return (await response.json()) as T;
}

export async function listOrders(filter: OrderListFilter): Promise<OrderSummary[]> {
  const query = new URLSearchParams();

  if (filter.status) {
    query.set("status", filter.status);
  }

  if (filter.orderNumber) {
    query.set("orderNumber", filter.orderNumber);
  }

  const suffix = query.size > 0 ? `?${query}` : "";
  const orders = await getJson<OrderSummary[]>(`/api/operations/orders${suffix}`);

  // A 404 here means the API is not running in Development, where these routes are mapped.
  if (orders === null) {
    throw new OperationsApiError(
      "The operations endpoints are not available. They are only mapped when the API runs in Development.",
    );
  }

  return orders;
}

export function getOrder(orderId: string): Promise<OrderDetail | null> {
  return getJson<OrderDetail>(`/api/operations/orders/${encodeURIComponent(orderId)}`);
}
