import { KeyRound, Sparkles, Users } from "lucide-react";
import { DoubleTick } from "@/components/brand-mark";

/**
 * The chatbot page header: what happens to an incoming message, in the order it really runs.
 * The numbers are the engine's precedence — a keyword rule always beats the assistant, and the
 * team always beats both — so the sequence is information, not decoration.
 */
const STEPS = [
  {
    icon: KeyRound,
    title: "Your keyword rules",
    text: "Answer first, word for word, when a message matches.",
  },
  {
    icon: Sparkles,
    title: "The AI assistant",
    text: "Handles everything else in the customer's language, and books appointments.",
  },
  {
    icon: Users,
    title: "Your team",
    text: "Takes over when the assistant hands a chat on, or when you step in.",
  },
];

export function ChatbotHero() {
  return (
    <section className="relative overflow-hidden rounded-2xl bg-[#0f6b3e] px-6 py-7 text-[#f2faee] sm:px-8">
      <DoubleTick className="pointer-events-none absolute -right-8 -bottom-28 w-[24rem] text-[#0b5a33]" />

      <div className="relative max-w-2xl">
        <h1 className="text-[2rem] leading-tight font-light tracking-[-0.03em]">Chatbot</h1>
        <p className="mt-1.5 text-[14px] leading-relaxed text-[#b9dcc5]">
          Replies to incoming WhatsApp messages for you. It never messages anyone who has opted out,
          and never talks over a person on your team.
        </p>
      </div>

      <ol className="relative mt-6 grid gap-3 md:grid-cols-3" aria-label="How a reply is decided">
        {STEPS.map((step, i) => (
          <li key={step.title} className="flex gap-3 rounded-xl bg-white/[0.07] p-4 ring-1 ring-white/10">
            <span className="flex size-8 shrink-0 items-center justify-center rounded-full bg-[#f2faee] text-[13px] font-semibold text-[#0f6b3e] tabular-nums">
              {i + 1}
            </span>
            <div className="min-w-0">
              <p className="flex items-center gap-1.5 text-[14px] font-medium">
                <step.icon className="size-3.5 text-[#b9dcc5]" aria-hidden />
                {step.title}
              </p>
              <p className="mt-1 text-[12.5px] leading-relaxed text-[#b9dcc5]">{step.text}</p>
            </div>
          </li>
        ))}
      </ol>
    </section>
  );
}
