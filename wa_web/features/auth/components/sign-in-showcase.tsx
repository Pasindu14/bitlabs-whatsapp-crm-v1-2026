import { SendHorizontal } from "lucide-react";
import { BrandMark, DoubleTick } from "@/components/brand-mark";
import { cn } from "@/lib/utils";
import s from "./sign-in-showcase.module.css";

/** Single tick, same stroke language as the brand's double tick. */
function SingleTick({ className }: { className?: string }) {
  return (
    <svg
      viewBox="0 0 17 12"
      className={className}
      fill="none"
      stroke="currentColor"
      strokeWidth={2}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden
    >
      <path d="M3 6.5 6.2 10 12 2.5" />
    </svg>
  );
}

/** Sent -> delivered -> read, animated once in CSS. */
function Ticks() {
  return (
    <span className={s.ticks} aria-hidden>
      <SingleTick className={s.tSent} />
      <DoubleTick className={s.tDelivered} />
      <DoubleTick className={s.tRead} />
    </span>
  );
}

/**
 * A Growchat inbox thread in miniature: campaign reminder, customer reply, chatbot confirmation.
 * Decorative only — the headline says the same thing in words.
 */
function ChatWindow() {
  return (
    <div aria-hidden className={cn(s.window, "w-full max-w-[23.5rem] overflow-hidden rounded-[1.5rem]")}>
      {/* Thread header, as the inbox shows it */}
      <div className={cn(s.windowHead, "flex items-center gap-3 px-4 py-3")}>
        <span className={cn(s.avatar, "flex size-9 shrink-0 items-center justify-center rounded-full text-[13px] font-medium")}>
          MC
        </span>
        <div className="min-w-0 flex-1 leading-tight">
          <p className="truncate text-[14px] font-medium">Mia Carter</p>
          <p className={cn(s.meta, "text-[12px]")}>+971 50 482 1176</p>
        </div>
        <span className={cn(s.botPill, "flex items-center gap-1.5 rounded-full px-2.5 py-1 text-[11px] font-medium")}>
          <span className={s.liveDot} />
          Chatbot replying
        </span>
      </div>

      {/* Thread */}
      <div className={cn(s.windowBody, "flex flex-col gap-2 px-3.5 py-4 text-[13.5px] leading-snug")}>
        <span className={cn(s.dayChip, "mx-auto mb-1 rounded-md px-2 py-0.5 text-[11px]")}>Today</span>

        <div className={cn(s.bubble, s.bubbleOut, s.b1, "ml-auto max-w-[85%] rounded-2xl rounded-br-md px-3 py-2")}>
          <p>
            Hi Mia, a reminder that your cleaning at Lotus Dental is tomorrow at 10:30 am.
            Reply 1 to confirm or 2 to reschedule.
          </p>
          <p className={cn(s.meta, "mt-1 flex items-center justify-end gap-1 text-[11px]")}>
            10:42 <Ticks />
          </p>
        </div>

        <div className={cn(s.bubble, s.bubbleIn, s.b2, "mr-auto rounded-2xl rounded-bl-md px-3 py-1.5")}>
          <p>1</p>
          <p className={cn(s.meta, "text-right text-[11px]")}>10:44</p>
        </div>

        {/* Typing dots: shown while the chatbot "writes", then replaced by its reply. */}
        <div className={cn(s.typing, "ml-auto flex gap-1 rounded-2xl rounded-br-md px-3 py-2.5")}>
          <span />
          <span />
          <span />
        </div>

        <div className={cn(s.bubble, s.bubbleOut, s.b3, "ml-auto max-w-[85%] rounded-2xl rounded-br-md px-3 py-2")}>
          <p>Thanks, you&apos;re confirmed for 10:30 tomorrow. See you then!</p>
          <p className={cn(s.meta, "mt-1 flex items-center justify-end gap-1 text-[11px]")}>
            <span className={s.bot}>Chatbot</span>
            <span>10:44</span>
            <span className={s.ticks}>
              <DoubleTick className={s.tRead} />
            </span>
          </p>
        </div>
      </div>

      {/* Message bar */}
      <div className={cn(s.windowHead, "flex items-center gap-2 px-3 py-2.5")}>
        <span className={cn(s.composer, "flex-1 rounded-full px-3.5 py-2 text-[13px]")}>Type a message</span>
        <span className={cn(s.send, "flex size-8 items-center justify-center rounded-full")}>
          <SendHorizontal className="size-4" />
        </span>
      </div>
    </div>
  );
}

/** What the two cards show, in the order it happens. */
const STEPS = [
  { title: "Send a campaign", text: "Pick an approved template and a contact list. Growchat sends it to everyone." },
  { title: "Watch it land", text: "See it delivered, then ticks turn blue as each person reads it." },
  { title: "Answer every reply", text: "Replies arrive in one inbox. The chatbot answers and books; your team steps in when needed." },
];

/** Recipients shown in the avatar stack: initials and their tone (same palette as the inbox). */
const RECIPIENTS = [
  ["MC", "bg-[#dcf1e3] text-[#0f6b3e]"],
  ["JR", "bg-[#dcebfa] text-[#1b5e9e]"],
  ["AS", "bg-[#fbe7d3] text-[#9a4f0f]"],
  ["LK", "bg-[#ede3fa] text-[#5b3a9e]"],
  ["OB", "bg-[#fadce4] text-[#9e2b4b]"],
] as const;

/**
 * A bulk campaign in miniature: one template sent to 312 people, its delivery and read bars filling
 * in step with the headline words. Decorative — the headline and subtitle say the same in words.
 */
function CampaignCard() {
  return (
    <div aria-hidden className={cn(s.campaign, "relative w-[21rem] rounded-[1.25rem] p-4")}>
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-[14px] font-medium">Appointment reminders</p>
          <p className={cn(s.meta, "text-[12px]")}>Campaign to 312 patients</p>
        </div>
        <span className={cn(s.botPill, "rounded-full px-2.5 py-1 text-[11px] font-medium")}>Sending</span>
      </div>

      <div className="mt-3 flex items-center">
        {RECIPIENTS.map(([initials, tone], i) => (
          <span
            key={initials}
            className={cn("flex size-7 shrink-0 items-center justify-center rounded-full text-[9.5px] font-semibold ring-2 ring-white", tone, i > 0 && "-ml-1.5")}
          >
            {initials}
          </span>
        ))}
        <span className={cn(s.meta, "ml-2 text-[12px]")}>and 307 more</span>
      </div>

      <div className="mt-3.5 space-y-2 text-[12px]">
        <Bar label="Delivered" value="305" pct={98} className={s.barDelivered} />
        <Bar label="Read" value="268" pct={86} className={s.barRead} />
        <Bar label="Replied" value="74" pct={24} className={s.barReplied} />
      </div>
    </div>
  );
}

function Bar({ label, value, pct, className }: { label: string; value: string; pct: number; className: string }) {
  return (
    <div className="grid grid-cols-[4.5rem_minmax(0,1fr)_2rem] items-center gap-2">
      <span className={s.meta}>{label}</span>
      <span className="h-1.5 overflow-hidden rounded-full bg-[#e8efe9]">
        <span className={cn("block h-full rounded-full", className)} style={{ ["--w" as string]: `${pct}%` }} />
      </span>
      <span className="text-right font-medium tabular-nums">{value}</span>
    </div>
  );
}

/** Left panel of the sign-in page: the brand, what Growchat does, and the product in action. */
export function SignInShowcase() {
  return (
    <section className={cn(s.panel, "relative flex h-full flex-col justify-between gap-10 p-10 xl:p-14")}>
      {/* The brand's double tick, drawn huge and tone-on-tone behind everything. */}
      <DoubleTick className={s.watermark} />

      <div className="relative flex items-center justify-center gap-5 self-center">
        <BrandMark onDark className="size-21" />
        <span className="text-[3rem] font-medium tracking-tight">Growchat</span>
      </div>

      {/* Side by side at every desktop width. On narrower panels the cards are zoomed down (zoom
          shrinks their layout box too, unlike a transform) so nothing drops below the fold. */}
      <div className="relative grid grid-cols-[minmax(0,1fr)_auto] items-center gap-6 xl:gap-10">
        <div className="space-y-5 xl:space-y-6">
          <h1 className="text-[clamp(2.4rem,3.6vw,3.25rem)] leading-[0.96] font-light tracking-[-0.035em] xl:text-[clamp(3rem,4.4vw,4.75rem)]">
            <span className={cn(s.word, "block")}>Sent.</span>
            <span className={cn(s.word, "block")}>Delivered.</span>
            <span className={cn(s.word, "block")}>Read.</span>
          </h1>
          <p className={cn(s.soft, "max-w-[24rem] text-[13.5px] leading-relaxed xl:text-[15px]")}>
            Send one WhatsApp campaign to hundreds of customers at once, answer every reply from one
            inbox, and let the chatbot confirm bookings while you&apos;re busy.
          </p>

          {/* The cards on the right, told as the three steps they show. Each step lights up as its
              part of the animation plays, so text and picture read as one story. */}
          <ol className="relative max-w-[24rem] space-y-1 pt-1" aria-label="How Growchat works">
            {STEPS.map((step, i) => (
              <li key={step.title} className={cn(s.step, "relative flex gap-3.5 py-2")}>
                {i < STEPS.length - 1 && (
                  <span aria-hidden className="absolute top-10 bottom-[-0.5rem] left-[0.9375rem] w-px bg-white/15" />
                )}
                <span className={cn(s.stepNum, "relative flex size-[1.875rem] shrink-0 items-center justify-center rounded-full text-[13px] font-semibold tabular-nums")}>
                  {i + 1}
                </span>
                <div className="pt-0.5">
                  <p className="text-[14.5px] font-medium">{step.title}</p>
                  <p className={cn(s.soft, "mt-0.5 text-[13px] leading-relaxed")}>{step.text}</p>
                </div>
              </li>
            ))}
          </ol>
        </div>

        {/* The campaign goes out to many; one of the replies is handled in the chat in front. */}
        <div className="relative w-[25rem] pb-2 [zoom:0.68] min-[1200px]:[zoom:0.8] xl:[zoom:0.9] 2xl:[zoom:1]">
          <CampaignCard />
          {/* Overlaps only the card's bottom edge, so all three bars stay readable. */}
          <div className="relative z-[1] -mt-3 ml-10">
            <ChatWindow />
          </div>
        </div>
      </div>

      <p className={cn(s.soft, "relative text-[13px]")}>
        Made for clinics, salons and shops in Sri Lanka and the UAE.
      </p>
    </section>
  );
}
