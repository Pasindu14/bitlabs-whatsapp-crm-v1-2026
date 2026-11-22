import { Providers } from "@/providers";
import "./globals.css";
import type { Metadata } from "next";
import { Inter, Manrope } from "next/font/google";
import { cn } from "@/lib/utils";
import NextTopLoader from "nextjs-toploader";

export const metadata: Metadata = {
  title: "Growchat",
  description: "Growchat",
};

// Inter for the interface, Manrope for headings.
const inter = Inter({ subsets: ['latin'], variable: '--font-sans', display: 'swap' });
const manrope = Manrope({ subsets: ['latin'], variable: '--font-heading', display: 'swap' });

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html lang="en" suppressHydrationWarning className={cn("font-sans", inter.variable, manrope.variable)}>
      <body className={`${inter.className} antialiased`}>
        <NextTopLoader color="#0e7e4e" showSpinner={false} height={2} />
        <Providers>{children}</Providers>
      </body>
    </html>
  );
}
