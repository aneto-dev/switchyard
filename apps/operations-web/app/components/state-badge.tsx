import { humanise } from "@/lib/format";

// States an operator should look at, as opposed to ones the workflow is still progressing through.
const attentionStates = new Set([
  "NeedsAttention",
  "PaymentIndeterminate",
  "Indeterminate",
  "CompensatingInventory",
  "AwaitingRelease",
]);
const failedStates = new Set(["Failed", "Rejected", "Expired"]);
const settledStates = new Set(["Confirmed", "Authorised", "Reserved", "Released"]);

function toneFor(state: string): string {
  if (attentionStates.has(state)) {
    return "border-amber-400/40 bg-amber-400/10 text-amber-200";
  }

  if (failedStates.has(state)) {
    return "border-rose-400/40 bg-rose-400/10 text-rose-200";
  }

  if (settledStates.has(state)) {
    return "border-emerald-400/40 bg-emerald-400/10 text-emerald-200";
  }

  return "border-[var(--border)] bg-white/5 text-[var(--muted)]";
}

export function StateBadge({ state }: { state: string | null }) {
  if (!state) {
    return <span className="text-[var(--muted)]">—</span>;
  }

  return (
    <span className={`inline-flex whitespace-nowrap rounded-full border px-2.5 py-0.5 text-xs font-medium ${toneFor(state)}`}>
      {humanise(state)}
    </span>
  );
}
