import { Providers } from "@/providers";
import "./globals.css";
import type { Metadata } from "next";
import { Inter } from "next/font/google";
import { cn } from "@/lib/utils";
import NextTopLoader from "nextjs-toploader";

export const metadata: Metadata = {
  title: "Growchat",
  description: "Growchat",
};

// Font comes from the shadcn preset (Inter for everything, headings included).
const inter = Inter({ subsets: ['latin'], variable: '--font-sans' });

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html lang="en" suppressHydrationWarning className={cn("font-sans", inter.variable)}>
      <body className={`${inter.className} antialiased`}>
        <NextTopLoader color="var(--primary)" showSpinner={false} height={2} />
        <Providers>{children}</Providers>
      </body>
    </html>
  );
}
