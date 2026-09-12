import type { Page } from '@playwright/test'
import {
  DEMO_COMPANY,
  DEMO_MESSAGES,
  DEMO_NAMES,
  DEMO_PHONES,
  DEMO_PREVIEWS,
} from './demo-data'

/**
 * Freeze the page for a deterministic screenshot: kill animations, hide carets and scrollbars,
 * force the light theme, and drop hover/focus rings.
 */
export async function freezePage(page: Page) {
  await page.addStyleTag({
    content: `
      *, *::before, *::after {
        animation: none !important;
        transition: none !important;
        caret-color: transparent !important;
      }
      ::-webkit-scrollbar { width: 0 !important; height: 0 !important; }
      * { scrollbar-width: none !important; }
      [data-slot="skeleton"] { opacity: 0 !important; }
    `,
  })
  await page.emulateMedia({ colorScheme: 'light', reducedMotion: 'reduce' })
  await page.evaluate(() => {
    document.documentElement.classList.remove('dark')
    if (document.activeElement instanceof HTMLElement) document.activeElement.blur()
  })
}

/**
 * Overwrite every piece of real customer data on the page with fixed demo values.
 *
 * This is the last line of defence before a public marketing asset: the capture runs against the
 * live production tenant, so /inbox and /contacts render real names, phone numbers and message
 * bodies. Anything missed here ends up in the ad.
 */
export async function scrubPii(page: Page) {
  await page.evaluate(
    ({ names, phones, previews, messages, company }) => {
      const initials = (name: string) => {
        const parts = name.trim().split(/\s+/).filter(Boolean)
        if (parts.length === 0) return '?'
        if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase()
        return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase()
      }

      // Track the demo name assigned to each real name so the same person reads consistently
      // across the conversation list, the thread header, and any table.
      const nameMap = new Map<string, string>()
      let nameCursor = 0
      const demoNameFor = (real: string) => {
        const key = real.trim()
        if (!key) return key
        if (!nameMap.has(key)) nameMap.set(key, names[nameCursor++ % names.length])
        return nameMap.get(key)!
      }

      // ── 1. Conversation list rows ────────────────────────────────────────
      document.querySelectorAll<HTMLElement>('ul.divide-y > li > button').forEach((btn, i) => {
        const truncated = btn.querySelectorAll<HTMLElement>('span.truncate')
        if (truncated[0]) {
          const demo = demoNameFor(truncated[0].textContent ?? '')
          truncated[0].textContent = demo
          const fallback = btn.querySelector<HTMLElement>('[data-slot="avatar-fallback"]')
          if (fallback) fallback.textContent = initials(demo)
        }
        if (truncated[1]) truncated[1].textContent = previews[i % previews.length]
      })

      // ── 2. Thread header (name + phone line) ─────────────────────────────
      document.querySelectorAll<HTMLElement>('p.truncate.font-semibold').forEach((el) => {
        const demo = demoNameFor(el.textContent ?? '')
        el.textContent = demo
        const header = el.closest('div')?.parentElement
        const fallback = header?.querySelector<HTMLElement>('[data-slot="avatar-fallback"]')
        if (fallback) fallback.textContent = initials(demo)
      })

      // ── 3. Message bubbles ───────────────────────────────────────────────
      document.querySelectorAll<HTMLElement>('p.whitespace-pre-wrap').forEach((el, i) => {
        el.textContent = messages[i % messages.length]
      })

      // Customer-sent media could be anything. Never ship it.
      document.querySelectorAll<HTMLImageElement>('img[src^="/api/media/"]').forEach((img) => {
        img.removeAttribute('src')
        img.style.display = 'none'
      })

      // ── 4. Global sweep over every remaining text node ───────────────────
      // Catches contact tables, campaign recipient counts, tooltips, aria labels — anything the
      // structural passes above do not know about.
      const phoneRe = /\+?\d[\d\s().-]{7,}\d/g
      const emailRe = /[\w.+-]+@[\w-]+\.[\w.]+/g

      const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT)
      let phoneCursor = 0
      const seenPhones = new Map<string, string>()
      const nodes: Text[] = []
      let node: Node | null
      while ((node = walker.nextNode())) nodes.push(node as Text)

      for (const text of nodes) {
        const parentTag = text.parentElement?.tagName
        if (parentTag === 'SCRIPT' || parentTag === 'STYLE') continue
        const value = text.nodeValue ?? ''
        if (!value.trim()) continue

        let next = value.replace(phoneRe, (match) => {
          const compact = match.replace(/\D/g, '')
          // Don't mangle short numerics, dates, or money — only real phone-length runs.
          if (compact.length < 9 || compact.length > 15) return match
          if (!seenPhones.has(compact)) {
            seenPhones.set(compact, phones[phoneCursor++ % phones.length])
          }
          return seenPhones.get(compact)!
        })
        next = next.replace(emailRe, 'hello@northwind.example')

        // Any real name we already mapped, wherever else it appears.
        for (const [real, demo] of nameMap) {
          if (real.length > 3 && next.includes(real)) next = next.split(real).join(demo)
        }

        if (next !== value) text.nodeValue = next
      }

      // ── 5. Tenant identity in the sidebar / header ───────────────────────
      document.querySelectorAll<HTMLElement>('[data-capture-company]').forEach((el) => {
        el.textContent = company
      })
    },
    {
      names: DEMO_NAMES,
      phones: DEMO_PHONES,
      previews: DEMO_PREVIEWS,
      messages: DEMO_MESSAGES,
      company: DEMO_COMPANY,
    }
  )
}
