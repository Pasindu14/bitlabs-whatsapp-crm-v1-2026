import { Providers } from "@/providers";
import "./globals.css";
import type { Metadata } from "next";
import { Jost } from "next/font/google";
import { cn } from "@/lib/utils";
import NextTopLoader from "nextjs-toploader";

export const metadata: Metadata = {
  title: "Growchat",
  description: "Growchat",
};

// Jost for everything, headings included (chosen over the preset's Inter).
const jost = Jost({ subsets: ['latin'], variable: '--font-sans' });

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html lang="en" suppressHydrationWarning className={cn("font-sans", jost.variable)}>
      <body className={`${jost.className} antialiased`}>
        <NextTopLoader color="var(--primary)" showSpinner={false} height={2} />
        <Providers>{children}</Providers>
      </body>
    </html>
  );
}
