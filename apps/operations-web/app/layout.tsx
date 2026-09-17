import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "Operations experience | Switchyard",
  description: "Switchyard operations experience foundation.",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
