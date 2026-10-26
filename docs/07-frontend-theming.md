# 07 — Frontend & Theming

## Layout & assets

- `Views/Shared/_Layout.cshtml` — Bootstrap 5 navbar + themed shell. Inline script in
  `<head>` (before CSS paint) sets `data-bs-theme` from `localStorage` or system
  preference, avoiding a flash of the wrong mode. `meta[name="theme-color"]` carries
  `data-theme-color-light/dark` so the browser tab bar also switches.
- Toggle buttons (`data-theme-toggle`) appear in the navbar (desktop + mobile).
- `wwwroot/css/site.css` — Bootstrap 5 CSS variables overridden for dark mode:
  `[data-bs-theme="dark"]` (background/text/accent overrides).
- `wwwroot/js/site.js` — theme runtime:

  - `STORAGE_KEY = 'theme'`, persisted in `localStorage`.
  - Applies `data-bs-theme` on `documentElement`, writes localStorage, syncs
    `aria-pressed`/`aria-label`/`title` on every toggle for accessibility, updates
    `theme-color` meta, dispatches a `themechange` CustomEvent with `{ theme }`.
  - Uses one delegated click handler on `[data-theme-toggle]` (works for dynamically
    re-rendered toggles).
  - A `MutationObserver` re-syncs toggle state if the attribute changes from any source.
  - **Debug hotkey: `Ctrl+Shift+T`** toggles theme and dumps current state to the
    browser console (stored value, computed CSS vars, toggle aria state) — handy for
    verifying the theme pipeline.

## Analytics charts (Chart.js)

`Views/Analytics/Index.cshtml`:

- Loads `~/lib/chart.js/dist/chart.umd.min.js`.
- Renders `trafficChart` (line: page views, logins, registrations per day) and
  `loginChart` (bar: successful vs failed logins).
- Chart colors adapt to light/dark via `themeColors()` reading CSS custom properties;
  charts re-render whenever the page receives a `themechange` event so they always match
  the active theme.

## Client libraries (`wwwroot/lib`)

Bootstrap 5, jQuery (+ validation, unobtrusive), Chart.js, and icons (Bootstrap Icons)
shipped as static libs (no CDN).

## Accessibility notes

- Toggle buttons set `aria-pressed`, `aria-label`, `title` so screen readers can
  announce state and action.
- Chart canvases carry `role="img"` + descriptive `aria-label`.
- `theme-color` meta updated per theme for mobile browsers.