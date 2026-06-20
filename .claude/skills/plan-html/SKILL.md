---
name: plan-html
description: Generate a polished, self-contained HTML plan document with Jost font and a soft light color scheme. Use this skill whenever the user asks to generate, create, export, or render a plan as HTML, or when they want a visual/beautiful version of an implementation plan, task plan, feature plan, or any structured plan saved to the plan/ folder. Trigger even if the user just says "make an HTML plan", "render the plan", or "export plan to HTML".
---

# Plan HTML Generator

Convert a plan — either supplied inline, from an existing markdown file in the `plan/` folder, or created from context — into a polished, self-contained HTML document.

## Output target

Always save to `plan/<filename>.html` in the project root. Derive `<filename>` from the plan title or ticket ID (e.g., `PAS-13-campaign-quota-guard.html`). If the user specifies a name, use that exactly.

## Design system

### Typography
- **Font**: Jost (Google Fonts) — load via `<link href="https://fonts.googleapis.com/css2?family=Jost:wght@300;400;500;600;700&display=swap" rel="stylesheet">`
- Body: Jost 400, 15px/1.7
- Headings: Jost 600–700
- Code: `ui-monospace, 'Cascadia Code', Consolas, monospace`

### Palette (light, low-saturation)
```
--bg:          #f7f8fa      /* page background */
--surface:     #ffffff      /* card/section background */
--surface-alt: #f0f2f6      /* alternate surface (code bg, table header) */
--border:      #e2e6ee      /* borders and dividers */
--text:        #1a1d23      /* primary text */
--text-muted:  #6b7280      /* secondary text */
--accent:      #4f6ef7      /* primary accent (blue-indigo) */
--accent-soft: #eef0fd      /* accent tint for backgrounds */
--success:     #16a34a
--warning:     #d97706
--danger:      #dc2626
--tag-bg:      #e8ebf5
--tag-text:    #3b4a80
```

### Layout
- Max-width: `860px`, centered, `padding: 48px 24px`
- Sections separated by a `1px solid var(--border)` divider with `margin: 32px 0`
- Rounded cards: `border-radius: 10px`, `border: 1px solid var(--border)`

## HTML structure

Produce a **single self-contained `.html` file** — all CSS inlined in `<style>`. No external dependencies except the Google Fonts `<link>`.

### Template skeleton

```html
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>{PLAN TITLE}</title>
  <link href="https://fonts.googleapis.com/css2?family=Jost:wght@300;400;500;600;700&display=swap" rel="stylesheet">
  <style>
    /* --- reset & base --- */
    *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
    :root {
      --bg: #f7f8fa; --surface: #ffffff; --surface-alt: #f0f2f6;
      --border: #e2e6ee; --text: #1a1d23; --text-muted: #6b7280;
      --accent: #4f6ef7; --accent-soft: #eef0fd;
      --success: #16a34a; --warning: #d97706; --danger: #dc2626;
      --tag-bg: #e8ebf5; --tag-text: #3b4a80;
      --radius: 10px; --font: 'Jost', system-ui, sans-serif;
      --mono: ui-monospace, 'Cascadia Code', Consolas, monospace;
    }
    body { font-family: var(--font); font-size: 15px; line-height: 1.7;
           color: var(--text); background: var(--bg); }
    a { color: var(--accent); text-decoration: none; }
    a:hover { text-decoration: underline; }

    /* --- layout --- */
    .page { max-width: 860px; margin: 0 auto; padding: 48px 24px 80px; }

    /* --- header --- */
    .plan-header { margin-bottom: 40px; }
    .plan-tag { display: inline-block; background: var(--tag-bg); color: var(--tag-text);
                font-size: 12px; font-weight: 600; letter-spacing: .05em; text-transform: uppercase;
                padding: 4px 10px; border-radius: 20px; margin-bottom: 14px; }
    .plan-title { font-size: 28px; font-weight: 700; color: var(--text); line-height: 1.3; }
    .plan-subtitle { margin-top: 10px; color: var(--text-muted); font-size: 15px; }
    .plan-meta { display: flex; gap: 20px; margin-top: 16px; flex-wrap: wrap; }
    .plan-meta-item { font-size: 13px; color: var(--text-muted); }
    .plan-meta-item strong { color: var(--text); font-weight: 600; }

    /* --- divider --- */
    hr { border: none; border-top: 1px solid var(--border); margin: 32px 0; }

    /* --- headings --- */
    h2 { font-size: 18px; font-weight: 700; color: var(--text); margin: 36px 0 14px; }
    h3 { font-size: 15px; font-weight: 600; color: var(--text); margin: 24px 0 10px; }

    /* --- paragraphs & lists --- */
    p { margin-bottom: 12px; }
    ul, ol { padding-left: 22px; margin-bottom: 14px; }
    li { margin-bottom: 6px; }
    li::marker { color: var(--accent); }

    /* --- step cards --- */
    .step { background: var(--surface); border: 1px solid var(--border);
             border-radius: var(--radius); padding: 24px 28px; margin-bottom: 18px; }
    .step-label { font-size: 11px; font-weight: 700; letter-spacing: .08em;
                  text-transform: uppercase; color: var(--accent); margin-bottom: 8px; }
    .step-title { font-size: 16px; font-weight: 700; margin-bottom: 12px; }
    .step-file { display: inline-flex; align-items: center; gap: 6px;
                 background: var(--surface-alt); border: 1px solid var(--border);
                 border-radius: 6px; padding: 4px 10px; font-size: 12px; font-family: var(--mono);
                 color: var(--text-muted); margin-bottom: 14px; }

    /* --- code blocks --- */
    pre { background: var(--surface-alt); border: 1px solid var(--border);
          border-radius: 8px; padding: 16px 18px; overflow-x: auto;
          font-family: var(--mono); font-size: 13px; line-height: 1.6;
          color: var(--text); margin: 14px 0; }
    code { font-family: var(--mono); font-size: 13px;
           background: var(--surface-alt); padding: 2px 5px; border-radius: 4px; }
    pre code { background: none; padding: 0; }

    /* --- tables --- */
    .table-wrap { overflow-x: auto; margin: 16px 0; border-radius: var(--radius);
                   border: 1px solid var(--border); }
    table { width: 100%; border-collapse: collapse; font-size: 14px; }
    thead th { background: var(--surface-alt); font-weight: 600; text-align: left;
               padding: 10px 14px; border-bottom: 1px solid var(--border);
               color: var(--text-muted); font-size: 12px; letter-spacing: .04em;
               text-transform: uppercase; }
    tbody td { padding: 10px 14px; border-bottom: 1px solid var(--border); vertical-align: top; }
    tbody tr:last-child td { border-bottom: none; }
    tbody tr:hover { background: var(--surface-alt); }

    /* --- callout / info box --- */
    .callout { border-left: 3px solid var(--accent); background: var(--accent-soft);
               border-radius: 0 8px 8px 0; padding: 14px 18px; margin: 18px 0;
               font-size: 14px; }
    .callout.warning { border-color: var(--warning); background: #fef9ec; }
    .callout.danger  { border-color: var(--danger);  background: #fef2f2; }
    .callout.success { border-color: var(--success);  background: #f0fdf4; }

    /* --- implementation order / timeline --- */
    .order-list { list-style: none; padding: 0; margin: 0; }
    .order-item { display: flex; gap: 14px; align-items: flex-start; padding: 12px 0;
                  border-bottom: 1px solid var(--border); }
    .order-item:last-child { border-bottom: none; }
    .order-num { flex-shrink: 0; width: 28px; height: 28px; border-radius: 50%;
                 background: var(--accent); color: #fff; font-size: 12px; font-weight: 700;
                 display: flex; align-items: center; justify-content: center; margin-top: 2px; }
    .order-body { flex: 1; }
    .order-title { font-weight: 600; margin-bottom: 2px; }
    .order-desc  { font-size: 13px; color: var(--text-muted); }

    /* --- badge --- */
    .badge { display: inline-block; font-size: 11px; font-weight: 600; padding: 2px 8px;
             border-radius: 12px; }
    .badge-blue   { background: var(--accent-soft); color: var(--accent); }
    .badge-green  { background: #f0fdf4; color: var(--success); }
    .badge-orange { background: #fef9ec; color: var(--warning); }
    .badge-red    { background: #fef2f2; color: var(--danger); }

    /* --- files touched --- */
    .files-grid { display: grid; gap: 8px; margin-top: 12px; }
    .file-row { display: flex; gap: 12px; align-items: flex-start; background: var(--surface);
                border: 1px solid var(--border); border-radius: 8px; padding: 10px 14px; }
    .file-path { font-family: var(--mono); font-size: 12px; color: var(--accent);
                 font-weight: 500; flex-shrink: 0; }
    .file-change { font-size: 13px; color: var(--text-muted); }

    /* --- footer --- */
    .plan-footer { margin-top: 56px; padding-top: 20px; border-top: 1px solid var(--border);
                   font-size: 12px; color: var(--text-muted); display: flex;
                   justify-content: space-between; flex-wrap: wrap; gap: 8px; }
  </style>
</head>
<body>
  <div class="page">
    <!-- HEADER -->
    <header class="plan-header">
      <div class="plan-tag">{TICKET-ID or "Implementation Plan"}</div>
      <h1 class="plan-title">{PLAN TITLE}</h1>
      <p class="plan-subtitle">{ONE-SENTENCE SUMMARY}</p>
      <!-- optional meta row -->
      <div class="plan-meta">
        <div class="plan-meta-item"><strong>Status</strong> Draft</div>
        <div class="plan-meta-item"><strong>Steps</strong> {N}</div>
      </div>
    </header>

    <hr>

    <!-- BACKGROUND SECTION -->
    <section>
      <h2>Background &amp; Root Cause</h2>
      <p>...</p>
      <!-- tables, callouts, lists go here -->
    </section>

    <hr>

    <!-- DECISIONS TABLE -->
    <section>
      <h2>Decisions</h2>
      <div class="table-wrap">
        <table>
          <thead><tr><th>Topic</th><th>Decision</th></tr></thead>
          <tbody>
            <tr><td>...</td><td>...</td></tr>
          </tbody>
        </table>
      </div>
    </section>

    <hr>

    <!-- STEPS (one .step card per step) -->
    <section>
      <h2>Implementation Steps</h2>

      <div class="step">
        <div class="step-label">Step 1</div>
        <div class="step-title">{STEP TITLE}</div>
        <div class="step-file">📄 path/to/File.cs</div>
        <p>...</p>
        <pre><code>{CODE SNIPPET}</code></pre>
      </div>

      <!-- repeat for each step -->
    </section>

    <hr>

    <!-- IMPLEMENTATION ORDER -->
    <section>
      <h2>Implementation Order</h2>
      <ul class="order-list">
        <li class="order-item">
          <div class="order-num">1</div>
          <div class="order-body">
            <div class="order-title">Step title</div>
            <div class="order-desc">Short rationale / why this comes first</div>
          </div>
        </li>
      </ul>
    </section>

    <hr>

    <!-- FILES TOUCHED -->
    <section>
      <h2>Files Touched</h2>
      <div class="files-grid">
        <div class="file-row">
          <span class="file-path">path/to/File.cs</span>
          <span class="file-change">What changes</span>
        </div>
      </div>
    </section>

    <hr>

    <!-- OUT OF SCOPE (optional) -->
    <section>
      <h2>Out of Scope</h2>
      <ul>
        <li>...</li>
      </ul>
    </section>

    <footer class="plan-footer">
      <span>Generated {DATE}</span>
      <span>{PROJECT NAME}</span>
    </footer>
  </div>
</body>
</html>
```

## Rendering rules

1. **Map markdown headings** → semantic HTML sections. `## Step N` → a `.step` card.
2. **Code fences** → `<pre><code>` blocks. Preserve indentation exactly.
3. **Tables** → `.table-wrap > table` with `thead`/`tbody`.
4. **Blockquotes or NOTE/WARNING lines** → `.callout` (add `.warning` or `.danger` class if signalled).
5. **Bold file paths** in step text → `.step-file` badge.
6. **"Implementation Order" section** → `.order-list` with numbered circles.
7. **"Files Touched" table** → `.files-grid` with `.file-row` items; file path in `.file-path` (monospace blue), description in `.file-change`.
8. Fill `{DATE}` with today's date in `DD MMM YYYY` format.
9. Fill `{PROJECT NAME}` from the project root folder name.
10. Inline all styles — no `<link>` other than Google Fonts.

## Process

1. Read the source plan (markdown file or inline content).
2. Parse sections and map to HTML components above.
3. Write the file to `plan/<name>.html`.
4. Confirm the file path to the user.

Do NOT open a browser or start a server — just write the file and report the path.
