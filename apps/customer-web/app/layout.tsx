import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "Customer experience | Switchyard",
  description: "Switchyard customer experience foundation.",
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
