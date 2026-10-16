# Avzar web

Next.js app. This step contains the PUBLIC SITE RENDERER: it renders each business's published storefront from its saved
configuration using a fixed component library (`src/components/sections.tsx`). The staff dashboard is partial: login/register/invitation, staff/roles/locations, customer workflows, order list/detail/create/edit plus quote/confirm/cancel UI, and partial production task list/detail/actions are present. The production view uses `/api/tasks` for managers and the caller-scoped `/api/my/tasks` endpoint for worker-only accounts. Daily production scheduling/notifications are not built yet; an initial `/storefront` draft editor is now present but is not a polished drag-and-drop visual builder. The order and production UI await a fresh typecheck, test run and production build.

## How a request is served
1. `middleware.ts` maps the host to a site: `{slug}.BASE_DOMAIN`, or a verified custom domain (asks the API `/public/resolve`).
   It also sets a per-request nonce and a strict Content-Security-Policy.
2. The page fetches the published snapshot (`/public/sites/{slug}`) and renders it. Designs come from `/public/sites/{slug}/designs`.
3. Forms post to `/api/inquiry/{slug}`, which forwards to the API with the visitor's address (for rate limiting).

## Local
```bash
cp .env.example .env.local
npm ci
npm run dev          # http://localhost:3000/sites/<slug>   (no DNS needed)
npm run typecheck && npm test
```
Subdomains locally: use `<slug>.localhost:3000` with `BASE_DOMAIN=localhost` (browsers resolve *.localhost).

## Safety rules this code follows
- Text is only ever rendered as React text nodes (escaped). The ONE `dangerouslySetInnerHTML` is the JSON-LD block, which is
  `JSON.stringify` output with `<` escaped.
- Links come only from `resolveLink` (page id, form action, or https). Everything else becomes `#`.
- Theme colours/fonts are re-validated in `lib/theme.ts` before becoming CSS variables.
- Unknown section types are skipped, not rendered.

## Storefront management
The signed-in `/storefront` route uses the same-origin `/backend` proxy and the existing storefront API. Draft saves include `expectedRevision`; preview links are private and publishing/rollback require the relevant permission. The current editor exposes section content and theme configuration as JSON; it does not yet include a polished drag-and-drop canvas, media selection, custom domain management, or template-switch confirmation UI. Section presets can be added and sections can be reordered/removed.

The `/settings` route edits the business's seven-day opening-hours pattern and closure dates through the existing schedule API. It does not yet schedule production jobs or send reminders.

The initial finance UI lives at `/finance`, `/finance/invoices/new`, and `/finance/invoices/[id]`. It uses idempotency keys for invoice/payment creation and the backend's permission/state rules for issuing, voiding, credit notes and payment verification. It is not yet a complete finance module: expense/category CRUD and invoice draft editing remain open.
