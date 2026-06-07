import { LoginForm } from "@/features/auth/components/forms/login-form"
import { MessageCircle, Users, BarChart3, Zap } from "lucide-react"

export default function LoginPage() {
  return (
    <div className="min-h-svh grid lg:grid-cols-2">

      {/* ── Left brand panel ── */}
      <div className="relative hidden lg:flex flex-col items-center justify-center overflow-hidden p-12"
        style={{ background: "linear-gradient(135deg, #064E3B 0%, #065F46 40%, #047857 100%)" }}>

        {/* Decorative blobs */}
        <div className="absolute -bottom-32 -left-32 h-80 w-80 rounded-full opacity-20"
          style={{ background: "#10B981" }} />
        <div className="absolute -top-20 -right-20 h-96 w-96 rounded-full opacity-10"
          style={{ background: "#34D399" }} />
        <div className="absolute bottom-1/4 right-8 h-56 w-56 rounded-full opacity-15"
          style={{ background: "#059669" }} />

        {/* Floating chat bubble decorations */}
        <div className="absolute top-16 left-12 flex flex-col gap-2 opacity-20">
          <div className="h-3 w-24 rounded-full bg-white" />
          <div className="h-3 w-16 rounded-full bg-white" />
        </div>
        <div className="absolute bottom-20 right-16 flex flex-col gap-2 opacity-20">
          <div className="h-3 w-20 rounded-full bg-white ml-auto" />
          <div className="h-3 w-28 rounded-full bg-white ml-auto" />
        </div>

        {/* Content */}
        <div className="relative z-10 flex flex-col items-center gap-10 text-center text-white max-w-sm">

          {/* Icon */}
          <div className="flex h-20 w-20 items-center justify-center rounded-3xl shadow-2xl"
            style={{ background: "#25D366" }}>
            <MessageCircle className="h-10 w-10 text-white fill-white" />
          </div>

          <div className="space-y-3">
            <h1 className="text-3xl font-bold tracking-tight">WhatsApp CRM</h1>
            <p className="text-base leading-relaxed" style={{ color: "#A7F3D0" }}>
              Multi-tenant messaging platform for bulk campaigns, live chat, and contact management — powered by Meta&apos;s official API.
            </p>
          </div>

          <div className="w-12 h-px" style={{ background: "#34D399" }} />

          {/* Feature bullets */}
          <div className="grid grid-cols-1 gap-3 w-full text-left">
            {[
              { icon: Zap, text: "Bulk campaigns with throttled delivery" },
              { icon: Users, text: "Multi-tenant company isolation" },
              { icon: BarChart3, text: "Delivery & cost analytics" },
              { icon: MessageCircle, text: "Live inbox with 24-hour window" },
            ].map(({ icon: Icon, text }) => (
              <div key={text} className="flex items-center gap-3">
                <div className="flex h-7 w-7 shrink-0 items-center justify-center rounded-lg"
                  style={{ background: "rgba(16,185,129,0.25)" }}>
                  <Icon className="h-3.5 w-3.5" style={{ color: "#6EE7B7" }} />
                </div>
                <span className="text-sm" style={{ color: "#D1FAE5" }}>{text}</span>
              </div>
            ))}
          </div>

          <p className="text-xs" style={{ color: "#6EE7B7" }}>
            Powered by Meta WhatsApp Cloud API
          </p>
        </div>
      </div>

      {/* ── Right form panel ── */}
      <div className="flex flex-col items-center justify-center bg-background p-8">
        <div className="w-full max-w-sm space-y-8">

          {/* Mobile-only icon */}
          <div className="flex justify-center lg:hidden">
            <div className="flex h-14 w-14 items-center justify-center rounded-2xl shadow-lg"
              style={{ background: "#25D366" }}>
              <MessageCircle className="h-7 w-7 text-white fill-white" />
            </div>
          </div>

          {/* Heading */}
          <div className="space-y-1.5">
            <h2 className="text-2xl font-bold tracking-tight">Welcome back</h2>
            <p className="text-sm text-muted-foreground">
              Sign in to your WhatsApp CRM account
            </p>
          </div>

          {/* Form */}
          <LoginForm />

          {/* Footer */}
          <p className="text-center text-xs text-muted-foreground">
            &copy; {new Date().getFullYear()} WhatsApp CRM. All rights reserved.
          </p>
        </div>
      </div>

    </div>
  )
}
