# Avzar web

Next.js app. This step contains the PUBLIC SITE RENDERER: it renders each business's published storefront from its saved
configuration using a fixed component library (`src/components/sections.tsx`). The dashboard (login, builder editor, orders...)
is not built yet.

## How a request is served
1. `middleware.ts` maps the host to a site: `{slug}.BASE_DOMAIN`, or a verified custom domain (asks the API `/public/resolve`).
   It also sets a per-request nonce and a strict Content-Security-Policy.
2. The page fetches the published snapshot (`/public/sites/{slug}`) and renders it. Designs come from `/public/sites/{slug}/designs`.
3. Forms post to `/api/inquiry/{slug}`, which forwards to the API with the visitor's address (for rate limiting).

## Local
```bash
cp .env.example .env.local
npm install
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
