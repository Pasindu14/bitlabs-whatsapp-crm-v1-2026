import { CircleHelp, ShieldCheck } from "lucide-react"
import { LoginForm } from "@/features/auth/components/forms/login-form"
import { SignInShowcase } from "@/features/auth/components/sign-in-showcase"
import { BrandMark } from "@/components/brand-mark"
import { Separator } from "@/components/ui/separator"

export default function LoginPage() {
  return (
    <div className="grid min-h-svh lg:grid-cols-[minmax(0,1.35fr)_minmax(0,1fr)]">
      <div className="hidden lg:block">
        <SignInShowcase />
      </div>

      {/* Mint with one soft green glow from the panel side, so the two halves read as one page. */}
      <main className="relative flex flex-col overflow-hidden bg-[#eef5f0] px-5 py-8 sm:px-10 dark:bg-background">
        <div
          aria-hidden
          className="pointer-events-none absolute -top-40 -left-40 size-[36rem] rounded-full bg-[radial-gradient(closest-side,rgb(15_107_62/0.16),transparent)]"
        />

        {/* Mobile keeps the brand; the showcase panel is desktop-only. */}
        <div className="relative flex items-center gap-3 lg:hidden">
          <BrandMark className="size-9" />
          <span className="text-[1.25rem] font-medium tracking-tight">Growchat</span>
        </div>

        <div className="relative flex flex-1 items-center justify-center py-10">
          <div className="w-full max-w-[25rem] rounded-[1.25rem] bg-card p-8 shadow-[0_1px_2px_rgb(16_60_36/0.06),0_30px_60px_-30px_rgb(16_60_36/0.35)] ring-1 ring-[rgb(16_60_36/0.06)] sm:p-10">
            <h2 className="text-[2.125rem] leading-[1.1] font-normal tracking-[-0.03em]">Welcome back</h2>
            <p className="mt-2.5 text-[15px] leading-relaxed text-muted-foreground">
              Sign in with the email your company admin invited you with.
            </p>

            <LoginForm className="mt-8" />

            <Separator className="my-7" />

            <p className="flex items-start gap-2.5 text-[13px] leading-relaxed text-muted-foreground">
              <CircleHelp className="mt-0.5 size-4 shrink-0" />
              Can&apos;t sign in? Ask your company admin to reset your password.
            </p>
          </div>
        </div>

        <div className="relative flex flex-col items-center gap-1.5 text-center text-[12px] leading-relaxed text-muted-foreground">
          <p className="flex items-center justify-center gap-2">
            <ShieldCheck className="size-3.5 shrink-0 text-primary" />
            Messages are sent through Meta&apos;s official WhatsApp Business Platform.
          </p>
          <p>&copy; {new Date().getFullYear()} Growchat</p>
        </div>
      </main>
    </div>
  )
}
