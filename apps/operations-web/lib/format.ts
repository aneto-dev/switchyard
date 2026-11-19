const dateTimeFormat = new Intl.DateTimeFormat("en-GB", {
  dateStyle: "medium",
  timeStyle: "medium",
  timeZone: "UTC",
});

export function formatDateTime(value: string | null): string {
  return value ? `${dateTimeFormat.format(new Date(value))} UTC` : "—";
}

export function formatMoney(amount: number, currency: string): string {
  return new Intl.NumberFormat("en-GB", { style: "currency", currency }).format(amount);
}

// "NeedsAttention" -> "Needs attention"
export function humanise(value: string | null): string {
  if (!value) {
    return "—";
  }

  const words = value.replace(/([a-z])([A-Z])/g, "$1 $2").toLowerCase();

  return words.charAt(0).toUpperCase() + words.slice(1);
}
