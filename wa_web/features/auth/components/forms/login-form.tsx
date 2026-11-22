"use client"

import { useState } from "react"
import { signIn } from "next-auth/react"
import { useRouter } from "next/navigation"
import { Loader2, Eye, EyeOff, Mail, LockKeyhole } from "lucide-react"
import { cn } from "@/lib/utils"
import { Button } from "@/components/ui/button"
import { Label } from "@/components/ui/label"
import {
  InputGroup,
  InputGroupAddon,
  InputGroupButton,
  InputGroupInput,
} from "@/components/ui/input-group"

// Taller, white fields with a green focus ring — the sign-in form's only custom control styling.
const FIELD =
  "h-11 overflow-hidden rounded-lg bg-white dark:bg-input/30has-[[data-slot=input-group-control]:focus-visible]:border-primary has-[[data-slot=input-group-control]:focus-visible]:ring-primary/20"
// Chrome paints autofilled inputs pale blue, which only covers the text area and splits the field.
// An inset shadow repaints it to match the field.
const CONTROL =
  "h-full text-[15px] md:text-[15px] autofill:shadow-[inset_0_0_0_1000px_var(--card)] autofill:[-webkit-text-fill-color:var(--foreground)]"

export function LoginForm({
  className,
  ...props
}: React.ComponentProps<"form">) {
  const [email, setEmail] = useState("")
  const [password, setPassword] = useState("")
  const [showPassword, setShowPassword] = useState(false)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const router = useRouter()

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setLoading(true)
    setError(null)

    try {
      const result = await signIn("credentials", {
        email,
        password,
        redirect: false,
      })

      if (result?.error) {
        setError("That email and password don't match. Check both and try again.")
      } else {
        router.push("/dashboard")
        router.refresh()
      }
    } catch {
      setError("Growchat couldn't be reached. Check your connection and try again.")
    } finally {
      setLoading(false)
    }
  }

  return (
    <form
      onSubmit={handleSubmit}
      className={cn("flex flex-col gap-5", className)}
      {...props}
    >
      <div className="grid gap-2">
        <Label htmlFor="email" className="text-[13px] font-medium">Work email</Label>
        <InputGroup className={FIELD}>
          <InputGroupAddon className="pl-3.5">
            <Mail className="size-4" />
          </InputGroupAddon>
          <InputGroupInput
            id="email"
            type="email"
            placeholder="you@company.com"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            required
            autoComplete="email"
            autoFocus
            aria-invalid={error ? true : undefined}
            className={CONTROL}
          />
        </InputGroup>
      </div>

      <div className="grid gap-2">
        <Label htmlFor="password" className="text-[13px] font-medium">Password</Label>
        <InputGroup className={FIELD}>
          <InputGroupAddon className="pl-3.5">
            <LockKeyhole className="size-4" />
          </InputGroupAddon>
          <InputGroupInput
            id="password"
            type={showPassword ? "text" : "password"}
            placeholder="Your password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
            autoComplete="current-password"
            aria-invalid={error ? true : undefined}
            className={CONTROL}
          />
          <InputGroupAddon align="inline-end" className="pr-2.5">
            <InputGroupButton
              size="icon-sm"
              onClick={() => setShowPassword((v) => !v)}
              aria-label={showPassword ? "Hide password" : "Show password"}
              aria-pressed={showPassword}
              className="text-muted-foreground"
            >
              {showPassword ? <EyeOff className="size-4" /> : <Eye className="size-4" />}
            </InputGroupButton>
          </InputGroupAddon>
        </InputGroup>
      </div>

      {error && (
        <p role="alert" className="text-[13px] leading-relaxed text-destructive">
          {error}
        </p>
      )}

      <Button type="submit" disabled={loading} className="mt-1 h-11 rounded-lg text-[15px]">
        {loading ? (
          <>
            <Loader2 className="animate-spin" data-icon="inline-start" /> Signing in…
          </>
        ) : (
          "Sign in"
        )}
      </Button>
    </form>
  )
}
