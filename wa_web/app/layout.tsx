import { Providers } from "@/providers";
import "./globals.css";
import type { Metadata } from "next";
import { Jost } from "next/font/google";
import { cn } from "@/lib/utils";
import NextTopLoader from "nextjs-toploader";

export const metadata: Metadata = {
  title: "Bitlabs Enterprise",
  description: "Bitlabs Enterprise",
};

const jost = Jost({ subsets: ['latin'], variable: '--font-sans' });

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html lang="en" suppressHydrationWarning className={cn("font-sans", jost.variable)}>
      <body className={`${jost.className} antialiased`}>
        <NextTopLoader color="#f97316" />
        <Providers>{children}</Providers>
      </body>
    </html>
  );
}
