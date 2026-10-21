# FeatureGallery routing

`IRouter.Url` is the application route, such as `/commands`, never a deployment
prefix. `UseGalleryAkbura()` registers one `MockRouter` through the existing
Akbura service-provider chain. Desktop, Android and iOS use this default.
The browser supplies a `BrowserRouter` instead, before Avalonia renders its view.
App, Router and Link all resolve that same service.

The component's directional states observe the service; they do not own another
route. Router uses a two-way alias so the diagnostics route picker still writes
through to `IRouter`. App and Link only observe it. Page templates and NotFound
are selected by the existing Router component, not by JavaScript.
`RoutingSources.cs` supplies typed getters for the current state-initializer
probe, which cannot resolve DSL-injected names directly. These getters simply
return the injected service; they add no storage or subscription mechanism.

## Browser startup and history

The source `index.html` sets `<base href="/">` before CSS and importmap for the
root-hosted SDK dev-server. This also lets direct URLs such as
`https://localhost:7169/utility-variants/` load the boot module and styles from
the application root. Deployment preparation replaces this local default with
the actual absolute gallery base; it is not a universal production prefix.

1. `main.js` reads `gallery-config.js`. An explicit absolute `baseUrl` wins;
   local development falls back to the directory of **the boot module**, not
   the current pathname.
2. .NET imports `browser-router.js` with `JSHost.ImportAsync` and `[JSImport]`.
   `JsBrowserUrl` wraps location, history and an `Action<string>` callback.
3. `BrowserRouter` reads the initial location synchronously before registration.
   The initial deep-linked page is therefore selected without an Overview flash.
4. Internal navigation calls `pushState` once, then changes `IRouter.Url`.
   A same-route assignment does neither. Push/replace do not emit `popstate`.
5. Back/Forward callbacks are delivered through Avalonia's UI dispatcher and
   update the logical route only; they never push another history entry.

For example, with base `https://host/Akbura/Gallery/`:

```text
Open .../commands/ -> Url = /commands
Click Overview    -> pushState(.../overview/) -> Url = /overview
Back              -> popstate(.../commands/) -> Url = /commands
```

The engine retains the router for the application lifetime.
`StartBrowserAppAsync` returns after setup, not at shutdown, so the router must
not be disposed on its successful return. Disposal explicitly removes the .NET
callback and JS subscription. JS shares a single native popstate listener and
removes it when the last subscriber is disposed. Component recreation does not
create another browser adapter.

## URL contract

- Logical routes have a leading slash, no trailing slash, and lowercase ASCII
  slug segments (letters, digits, `_`, `-`). An empty route means `/overview`.
- Unknown valid slugs remain intact and select the existing NotFound template.
- External URLs, protocol-relative URLs, backslashes, encoded segments,
  dot/traversal segments and empty inner segments are rejected by the setter.
- Browser locations must have the configured origin and base **path boundary**:
  `/GalleryElse/` is not inside `/Gallery/`. Base path matching is case-sensitive;
  logical page matching is case-insensitive.
- Query strings and fragments do not select a page. Startup canonicalization
  preserves them using `replaceState`; internal navigation constructs a clean
  route URL without them. Popstate does not rewrite the URL.
- Browser history paths use trailing slashes. Root, case and slash corrections
  at startup use replace, not push.

## Static hosting

After publishing the Browser project, run:

```powershell
node eng/Prepare-FeatureGallerySite.mjs <publish-directory>/wwwroot https://host/apps/gallery/
```

This writes runtime configuration and an absolute HTML `<base>` before asset
references, and emits `<route>/index.html` for each actual `Page Url` literal in
`Components/App.akbura`. Dynamic, unsafe or duplicate registrations fail the
preparation step instead of silently losing a deep link. No separate route list
is maintained. Each entry loads the same app and preserves the requested path.

The Pages workflow resolves the documentation site base from Pages CNAME (or
the checked-in CNAME), otherwise owner/repository; user-site repositories use
the domain root. It derives `Gallery/` from that site base and serializes it into
the deployed config with JSON escaping. Documentation keeps its own base and
404 page. No environment variables are read by the deployed browser.

Known routes support static-host refresh and new-tab opening. An arbitrary
unknown URL requested from a static host still uses the site's existing 404;
unknown routes reached within the running gallery use its NotFound component.
This intentionally does not replace the documentation's 404 with the gallery.

## Verification

```powershell
node --test eng/FeatureGalleryRouting.test.mjs
dotnet test src/Akbura.UnitTests/Akbura.UnitTests.csproj -c Debug --filter FullyQualifiedName~GalleryRouting
dotnet test src/Akbura.UnitTests/Akbura.UnitTests.csproj -c Release --filter FullyQualifiedName~GalleryRouting
```

The C# tests exercise the real router classes and actual Gallery MainView with
the existing Avalonia headless harness. JavaScript tests exercise the real
bridge and deployment preparation, but are not a substitute for a WASM test.

For browser verification, serve the prepared publish directory with a static
server (no SPA fallback). Repeat at `/`, `/Gallery/` and `/Akbura/Gallery/`,
preparing the matching absolute base each time. Open `commands/` directly,
click Overview, use Back and Forward, refresh, then copy the URL into a new tab.
Check the displayed page, current-route indicator and active link each time.
In DevTools, confirm no failed module/framework requests or console errors.
The `eng/Serve-FeatureGallery.mjs` helper hosts all three configurations locally.
