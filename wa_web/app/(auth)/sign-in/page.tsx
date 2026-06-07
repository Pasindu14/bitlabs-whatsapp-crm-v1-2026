import { LoginForm } from "@/features/auth/components/forms/login-form"
import { Separator } from "@/components/ui/separator"
import Image from "next/image"

export default function LoginPage() {
  return (
    <div className="min-h-svh grid lg:grid-cols-2">
      {/* ── Left brand panel ── */}
      <div className="relative hidden lg:flex flex-col items-center justify-center overflow-hidden bg-[#B91C1C] p-12">
        {/* Gradient overlay */}
        <div className="absolute inset-0 bg-gradient-to-br from-red-950 via-red-800 to-red-700" />

        {/* Decorative blobs */}
        <div className="absolute -bottom-24 -left-24 h-72 w-72 rounded-full bg-red-900/50" />
        <div className="absolute -top-24 -right-24 h-96 w-96 rounded-full bg-red-600/20" />
        <div className="absolute bottom-1/3 right-0 h-48 w-48 rounded-full bg-red-900/30" />

        {/* Content */}
        <div className="relative z-10 flex flex-col items-center gap-10 text-center text-white">
          <Image
            src="/logo.png"
            alt="Uswatte Biscuits"
            width={220}
            height={130}
            className="drop-shadow-2xl"
            priority
            unoptimized
          />

          <div className="space-y-3 max-w-xs">
            <h1 className="text-3xl font-bold tracking-tight">
              Sales Force Automation
            </h1>
            <p className="text-red-200 text-base leading-relaxed">
              Streamline your field operations, track your team, and grow your
              sales with confidence.
            </p>
          </div>

          <Separator className="w-20 bg-red-500/50" />

          <p className="text-red-300 text-sm">
            Uswatte Biscuits &mdash; Internal Portal
          </p>
        </div>
      </div>

      {/* ── Right form panel ── */}
      <div className="flex flex-col items-center justify-center bg-background p-8">
        <div className="w-full max-w-sm space-y-8">
          {/* Mobile-only logo */}
          <div className="flex justify-center lg:hidden">
            <Image
              src="/logo.png"
              alt="Uswatte Biscuits"
              width={150}
              height={90}
              priority
              unoptimized
            />
          </div>

          {/* Heading */}
          <div className="space-y-1">
            <h2 className="text-2xl font-bold tracking-tight">Welcome back</h2>
            <p className="text-sm text-muted-foreground">
              Sign in to your account to continue
            </p>
          </div>

          {/* Form */}
          <LoginForm />

          {/* Footer */}
          <p className="text-center text-xs text-muted-foreground">
            &copy; {new Date().getFullYear()} Uswatte Biscuits. All rights
            reserved.
          </p>
        </div>
      </div>
    </div>
  )
}
