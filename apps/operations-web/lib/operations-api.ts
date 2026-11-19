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

export type FulfilmentCapture = {
  status: "NotRequested" | "Pending" | "Captured" | "Indeterminate" | "NotApplied";
  requestId: string | null;
  requestedAtUtc: string | null;
  providerReference: string | null;
  resolvedAtUtc: string | null;
};

export type FulfilmentLine = {
  orderLineId: string;
  skuCode: string;
  productName: string;
  quantity: number;
  preparedQuantity: number;
  cancelledQuantity: number;
};

export type FulfilmentShipment = {
  shipmentId: string;
  itemQuantity: number;
  createdAtUtc: string;
  dispatchedAtUtc: string | null;
  deliveredAtUtc: string | null;
};

export type FulfilmentCancellation = {
  cancellationId: string;
  outcome: "Accepted" | "Rejected";
  rejectionReason: string | null;
  quantity: number;
  requestedAtUtc: string;
};

export type OrderFulfilment = {
  orderId: string;
  status: "Pending" | "PartiallyPrepared" | "Prepared";
  confirmedAtUtc: string;
  paymentId: string;
  capture: FulfilmentCapture;
  lines: FulfilmentLine[];
  shipments: FulfilmentShipment[];
  cancellations: FulfilmentCancellation[];
};

export type ReturnRefund = {
  status: "Pending" | "Indeterminate" | "Succeeded" | "NotApplied";
  amount: number;
  currency: string;
  providerReference: string | null;
  requestedAtUtc: string;
  updatedAtUtc: string;
};

export type ReturnReceipt = {
  returnReceiptId: string;
  quantity: number;
  receivedAtUtc: string;
  refund: ReturnRefund | null;
};

export type ReturnRequest = {
  returnRequestId: string;
  status: "Authorised" | "PartiallyReceived" | "Received";
  quantity: number;
  requestedAtUtc: string;
  receipts: ReturnReceipt[];
};

export type ReturnLine = {
  orderLineId: string;
  deliveredQuantity: number;
  // Authorised but not yet received.
  authorisedReturnQuantity: number;
  returnedQuantity: number;
};

export type OrderReturns = {
  orderId: string;
  lines: ReturnLine[];
  requests: ReturnRequest[];
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

// Null until Fulfilment has accepted the confirmed order.
export function getOrderFulfilment(orderId: string): Promise<OrderFulfilment | null> {
  return getJson<OrderFulfilment>(`/api/operations/orders/${encodeURIComponent(orderId)}/fulfilment`);
}

// Null until a shipment for the order has been delivered.
export function getOrderReturns(orderId: string): Promise<OrderReturns | null> {
  return getJson<OrderReturns>(`/api/operations/orders/${encodeURIComponent(orderId)}/returns`);
}
