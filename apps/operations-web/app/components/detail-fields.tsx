export function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <dt className="text-xs uppercase tracking-wide text-[var(--muted)]">{label}</dt>
      <dd className="mt-1 text-sm">{children}</dd>
    </div>
  );
}

export function Identifier({ value }: { value: string | null }) {
  return value ? (
    <span className="break-all font-mono text-xs">{value}</span>
  ) : (
    <span className="text-[var(--muted)]">—</span>
  );
}
