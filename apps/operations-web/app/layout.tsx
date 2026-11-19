import type { Metadata } from "next";
import Link from "next/link";
import "./globals.css";

export const metadata: Metadata = {
  title: {
    default: "Operations | Switchyard",
    template: "%s | Switchyard Operations",
  },
  description: "Switchyard operations view of orders and their placement workflow.",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en">
      <body>
        <header className="border-b border-[var(--border)] bg-[color:var(--surface)]/80">
          <div className="mx-auto flex max-w-6xl flex-wrap items-center justify-between gap-3 px-4 py-4 md:px-6">
            <Link href="/orders" className="text-sm font-semibold tracking-wide no-underline">
              Switchyard <span className="text-[var(--accent)]">Operations</span>
            </Link>
            <p className="text-xs text-[var(--muted)]">
              Local development only. Access control arrives with the security milestone.
            </p>
          </div>
        </header>
        <main className="mx-auto max-w-6xl px-4 py-8 md:px-6">{children}</main>
      </body>
    </html>
  );
}
