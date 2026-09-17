const milestones = [
  "Repository and runtime foundation",
  "Order domain and inventory reservation",
  "Payments and durable distributed workflows",
  "Fulfilment, returns and operational tooling",
];

export default function Home() {
  return (
    <main className="mx-auto flex min-h-screen max-w-5xl items-center px-6 py-16">
      <section className="w-full rounded-3xl border border-[var(--border)] bg-[color:var(--surface)]/90 p-8 shadow-2xl shadow-black/20 md:p-12">
        <p className="mb-4 text-sm font-semibold uppercase tracking-[0.22em] text-sky-300">
          Switchyard customer web
        </p>
        <h1 className="max-w-3xl text-4xl font-bold tracking-tight md:text-6xl">
          Customer experience
        </h1>
        <p className="mt-6 max-w-3xl text-lg leading-8 text-[var(--muted)]">
          The customer-facing product will eventually support realistic ordering, inventory reservation, payment and order-tracking journeys. For v0.1 this application only establishes the browser and BFF boundary.
        </p>

        <div className="mt-8 inline-flex rounded-full border border-sky-400/30 bg-sky-400/10 px-4 py-2 text-sm text-sky-200">
          v0.1 foundation - product workflows are not implemented yet
        </div>

        <div className="mt-10 grid gap-4 md:grid-cols-2">
          {milestones.map((milestone, index) => (
            <div
              key={milestone}
              className="rounded-2xl border border-[var(--border)] bg-black/10 p-5"
            >
              <span className="text-sm text-sky-300">0{index + 1}</span>
              <p className="mt-2 font-medium">{milestone}</p>
            </div>
          ))}
        </div>

        <p className="mt-10 max-w-3xl text-sm leading-7 text-[var(--muted)]">
          Boundary: customer identity and session concerns remain separate from operations access.
        </p>
      </section>
    </main>
  );
}
