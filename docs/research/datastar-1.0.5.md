# Datastar reference sheet (for ASP.NET Core / .NET 10 minimal APIs)

Gathered 2026-11-14 from primary sources only. Where a fact comes from reading the shipped bundle source rather than prose docs, it is marked (bundle).
Sources key:
- DOCS = https://data-star.dev (pages cited individually)
- BUNDLE = https://cdn.jsdelivr.net/gh/starfederation/datastar@v1.0.5/bundles/datastar.js (read the minified source)
- REL = https://github.com/starfederation/datastar/releases (and API https://api.github.com/repos/starfederation/datastar/releases)
- DOTNET = https://github.com/starfederation/datastar-dotnet (cloned at commit bec97852, 2026-06-09) and NuGet https://www.nuget.org/packages/StarFederation.Datastar
- ADR = https://github.com/starfederation/datastar/blob/main/sdk/ADR.md (SDK spec)

---------------------------------------------------------------------
## 1. Version, loading, licence

- Current stable: **v1.0.5**, published 2026-11-09 (REL, https://github.com/starfederation/datastar/releases/tag/v1.0.5). Earlier 1.0 line: v1.0.0 2026-04-16, 1.0.1 2026-04-20, 1.0.2 2026-06-02, 1.0.3 2026-08-27, 1.0.4 2026-09-21. No prerelease is flagged in the releases list. Default branch of the repo is `develop`; the 1.0.5 release page also mentions "Rocket beta.3" (Rocket = optional web-component layer, separate bundle, ignore it).
- Script tag (DOCS https://data-star.dev/guide/getting_started):
```html
<script type="module" src="https://cdn.jsdelivr.net/gh/starfederation/[email protected]/bundles/datastar.js"></script>
```
  (The docs page renders the address with an email-obfuscation placeholder; the real URL is `https://cdn.jsdelivr.net/gh/starfederation/datastar@1.0.5/bundles/datastar.js`. Verified the URL `.../datastar@v1.0.5/bundles/datastar.js` returns the file; first line is `// Datastar v1.0.5`.)
- Docs say: "Hosting the file yourself is recommended. Download the script or create your own bundle using the bundler" then `<script type="module" src="/path/to/datastar.js"></script>`. It is a single self-contained ES module file (no other chunks seen; no imports in the first line, no npm needed). Size I downloaded: **35,673 bytes minified, 14,134 bytes gzipped** (BUNDLE, measured locally).
- Other bundles: `datastar-aliased.js` (attributes become `data-star-*`, for conflicts), `datastar-rocket.js` (Datastar + Rocket). Not needed.
- Licence: **MIT** (GitHub API license key `mit` for starfederation/datastar). The datastar-dotnet repo has LICENSE.md (type "file" on NuGet; see section 6).
- Pro (paid, commercial licence) attributes and actions exist; see section 8 to avoid them.

---------------------------------------------------------------------
## 2. Attribute reference (v1.0.5)

Source for everything in this section: https://data-star.dev/reference/attributes (v1.0.5 reference). The key separator is a **colon**: `data-on:click`, `data-signals:foo`, `data-bind:foo`. Modifiers follow `__` and tags follow `.` : `data-on:click__debounce.500ms`. (Pre-1.0 betas used `data-on-click`, `data-signals-foo`, `data-merge-...`; do not use those. The docs and the SDK README for 1.0.5 both use the colon form.)

Casing rules (https://data-star.dev/reference/attributes#attribute-casing):
- HTML attribute names are case-insensitive, so write hyphens.
- Keys of attributes that DEFINE signals (`data-bind:*`, `data-signals:*`, `data-computed:*`, `data-indicator:*`, `data-ref:*`) are converted to camelCase: `data-signals:my-signal` -> `$mySignal`.
- Key suffixes of all other attributes (`data-class:*`, `data-attr:*`, `data-on:*`, `data-style:*`) are converted to kebab-case by default: `data-class:text-blue-700` toggles class `text-blue-700`; `data-on:rocket-launched` listens for event `rocket-launched`.
- `__case.camel|kebab|snake|pascal` modifier overrides: for an event named `widgetLoaded` write `data-on:widget-loaded__case.camel`.
- Signal names cannot begin with or contain a double underscore `__` (modifier delimiter).
- Attributes are evaluated depth-first, in the order they appear on the element. So `data-indicator:fetching` must come BEFORE a `data-init="@get(...)"` on the same element.

| Purpose | Exact spelling | One-line example |
|---|---|---|
| Define signals | `data-signals` / `data-signals:name` | `<div data-signals="{count: 0, user: {name: ''}}">` or `<div data-signals:count="0">`; nested with dots `data-signals:foo.bar="1"`; setting to `null`/`undefined` removes. JSON or JS-object literal both accepted. Modifier `__ifmissing` only sets if not already present. |
| Two-way bind | `data-bind:name` or `data-bind="name"` | `<input data-bind:first-name />` creates `$firstName` (initial value comes from the element unless a signal already exists). Modifiers `__case`, `__prop`, `__event` (e.g. `data-bind:is-checked__prop.checked__event.change` for custom elements). |
| Text | `data-text` | `<span data-text="$count"></span>` |
| Show/hide | `data-show` | `<div data-show="$open" style="display:none">` (add inline display:none to avoid flash) |
| Class | `data-class:name` or `data-class="{...}"` | `<div data-class:font-bold="$x == 'strong'">`, `data-class="{success: $ok, 'font-bold': $b}"` |
| Attribute | `data-attr:name` or `data-attr="{...}"` | `<button data-attr:disabled="$fetching">`, `data-attr="{'aria-label': $foo, disabled: $bar}"` |
| Style | `data-style:prop` or `data-style="{...}"` | `<div data-style:display="$hiding && 'none'">` |
| Event handler | `data-on:event` (colon) | `<button data-on:click="$count++">`; `evt` is the event object. Custom event: `data-on:my-event="$foo = evt.detail"`. A `data-on:submit` listener auto-prevents default form submit. |
| Run on load / when patched in | `data-init` | `<div data-init="@get('/feed')">` runs on page load, when the element is patched into the DOM, and whenever the attribute is modified. Modifiers `__delay.500ms`, `__viewtransition`. **There is no `data-on-load` in 1.0.x docs; use `data-init`.** |
| Loading flag | `data-indicator:name` or `data-indicator="name"` | `<button data-on:click="@get('/x')" data-indicator:fetching data-attr:disabled="$fetching">`; signal true while the request is in flight. |
| Element ref | `data-ref:name` / `data-ref="name"` | `<div data-ref:panel>` then `$panel.tagName` |
| Computed signal | `data-computed:name` | `<div data-computed:total="$a + $b">` (read-only; no side effects in it) |
| Side effect | `data-effect` | `<div data-effect="$total = $a + $b">` runs on load and whenever signals it reads change |
| Intersect | `data-on-intersect` | `<div data-on-intersect__once__full="@get('/more')">`; modifiers `__once __exit __half __full __threshold.25 __delay __debounce __throttle __viewtransition` |
| Interval | `data-on-interval` | `<div data-on-interval__duration.5s.leading="@get('/poll')">` (default 1s; `.leading` runs first tick immediately) |
| Signal change listener | `data-on-signal-patch` (+ `data-on-signal-patch-filter="{include: /^x/}"`) | `<div data-on-signal-patch="console.log(patch)">`; `patch` variable is in scope |
| Skip Datastar processing | `data-ignore` (`__self` = element only) | `<div data-ignore>` (use around untrusted/third-party content) |
| Skip morphing | `data-ignore-morph` | `<div data-ignore-morph>` the patcher leaves this element and children alone; remove by patching the element without the attribute |
| Keep an attribute across morphs | `data-preserve-attr` | `<details open data-preserve-attr="open">` (space-separated list) |
| Debug | `data-json-signals` | `<pre data-json-signals></pre>` dumps signals (`__terse`, filter `{include: /re/}`) |

`data-on` modifiers (full list): `__once __passive __capture` (built-in events only), `__case`, `__delay.<n>ms|s`, `__debounce.<t>[.leading][.notrailing]`, `__throttle.<t>[.noleading][.trailing]`, `__viewtransition`, `__window`, `__document`, `__outside`, `__prevent`, `__stop`. Example from the docs: `data-on:click__window__debounce.500ms.leading="$foo = ''"`. Timing tags accept `500ms` or `1s`.

Expressions (https://data-star.dev/guide/datastar_expressions): `$name` = signal; `el` = the element; `evt` in `data-on`; multiple statements need a `;` (newlines alone do not split); start the expression with `return` to bypass statement splitting; `@action()` = Datastar action.

Plugin signal prefix actions also exist: `@peek()`, `@setAll(value, {include: /re/, exclude: /re/})`, `@toggleAll({include: /re/})` (https://data-star.dev/reference/actions).

---------------------------------------------------------------------
## 3. Backend actions

Source: https://data-star.dev/reference/actions (v1.0.5) and BUNDLE (the `Ee(...)` fetch plugin).

Syntax: `@get(uri, options)`, `@post(uri, options)`, `@put(uri, options)`, `@patch(uri, options)`, `@delete(uri, options)`, and (new in 1.0.x) `@query(uri, options)` which sends an HTTP method literally named `QUERY` with signals in the body.
```html
<button data-on:click="@post('/orders/42/approve')">Approve</button>
<button data-on:click="@get('/endpoint', {openWhenHidden: true, filterSignals: {include: /^form\./}, headers: {'X-Csrf-Token': 'abc'}, retry: 'error', requestCancellation: 'disabled'})"></button>
```
Options (all verified in docs and bundle defaults):
| Option | Meaning / default |
|---|---|
| `contentType` | `'json'` (default: all non-filtered signals are sent) or `'form'` (find closest `<form>` or `selector`, run HTML5 validation, send form fields; **no signals sent**) |
| `selector` | CSS selector of the form to send when `contentType: 'form'`; default `null` = closest form |
| `filterSignals` | `{include: RegExp, exclude?: RegExp}`; default include `/.*/`, default exclude is signals whose path segment starts with `_` (bundle: `/(^|\.)_/`; docs text says `/(^_|\._).*/`) |
| `headers` | object of extra request headers |
| `openWhenHidden` | keep connection open when tab hidden. Default **false for GET**, **true for all other methods** |
| `payload` | override the sent payload with a custom object |
| `retry` | `'auto'` (default; network errors only), `'error'` (also 4xx/5xx), `'always'`, `'never'` |
| `retryInterval` 1000 ms, `retryScaler` 2, `retryMaxWait` 30000 ms, `retryMaxCount` 10 | back-off controls |
| `requestCancellation` | `'auto'` (default; a new request cancels an in-flight one with the same method AND URL), `'cleanup'`, `'disabled'`, or an `AbortController` |

Always sent: header `Datastar-Request: true` and `Accept: text/event-stream, text/html, application/json` (bundle). For JSON mode on non-GET/DELETE, `Content-Type: application/json`.

How signals travel (BUNDLE, function `ht=e=>!["GET","DELETE"].includes(e)`):
- **GET and DELETE**: JSON-encoded signals in query-string parameter named **`datastar`** (`?datastar=%7B%22count%22%3A1%7D`). The docs prose only mentions GET; the bundle and the .NET SDK both treat DELETE the same as GET.
- **POST / PUT / PATCH / QUERY**: JSON string in the request body (`Content-Type: application/json`).
- With `contentType: 'form'`: POST/PUT/PATCH put the form in the body as `application/x-www-form-urlencoded` (or the raw `FormData` if the `<form enctype="multipart/form-data">`); GET/DELETE append the fields to the query string (no `datastar` param). The clicked submit button's `name`/`value` is added. Native `checkValidity()` runs first and `reportValidity()` shows browser messages; if invalid, no request is sent unless the form has `novalidate`.

Server response handling (same page, "Response Handling"; confirmed in bundle): content-types
- `text/event-stream` -> Datastar SSE events (section 4)
- `text/html` -> treated as one patch-elements; optional response headers `datastar-selector`, `datastar-mode` (outer default; inner, remove, replace, prepend, append, before, after), `datastar-use-view-transition`, `datastar-view-transition-selector`
- `application/json` -> patch-signals; optional header `datastar-only-if-missing: true`
- `text/javascript` -> executed; optional header `datastar-script-attributes` (JSON object of script attributes)
- 204 No Content / empty is allowed.

Fetch lifecycle event: `data-on:datastar-fetch="evt.detail.type === 'error' && ..."`; types `started`, `finished`, `error`, `retrying`, `retries-failed`.

---------------------------------------------------------------------
## 4. SSE protocol (v1.0.5)

Source: https://data-star.dev/reference/sse_events (event names confirmed also by string scan of BUNDLE: only `datastar-patch-elements` and `datastar-patch-signals` exist; `datastar-merge-fragments`/`datastar-merge-signals`/`datastar-execute-script` are the older pre-1.0 names and are NOT present).

Response headers the server must set (ADR line 49): `Content-Type: text/event-stream`, `Cache-Control: no-cache`, and `Connection: keep-alive` on HTTP/1.1 only. Events end with a blank line.

### datastar-patch-elements
Data lines (any order; each prefixed `data: `; all optional except `elements` unless mode is `remove`):
| Line | Meaning |
|---|---|
| `data: selector #foo` | CSS selector of target; not needed for outer/replace modes when top-level elements have ids |
| `data: mode outer` | default, morph outer HTML (recommended). Others: `inner` (morph inner), `replace` (replace outer, no morph, state resets), `prepend`, `append`, `before`, `after`, `remove` |
| `data: namespace svg` / `mathml` | patch into SVG or MathML namespace |
| `data: useViewTransition true` | use View Transitions API (default false) |
| `data: viewTransitionSelector #main` | element for the view transition (default document) |
| `data: elements <html...>` | the HTML; **multi-line HTML = repeat `data: elements ` once per line** |
ADR (https://github.com/starfederation/datastar/blob/main/sdk/ADR.md) marks `outer` and `inner` as the morphed modes; `replace`, `prepend`, `append`, `before`, `after`, `remove` are not morphed.

Exact wire format (from the reference page and guide):
```
event: datastar-patch-elements
data: elements <div id="hal">
data: elements     I'm sorry, Dave. I'm afraid I can't do that.
data: elements </div>

```
(the final empty line is required.) Single-line form:
```
event: datastar-patch-elements
data: elements <div id="foo">Hello world!</div>

```
Append to a list:
```
event: datastar-patch-elements
data: selector #list
data: mode append
data: elements <li id="beat-3">Beat 3</li>

```
Remove:
```
event: datastar-patch-elements
data: selector #foo
data: mode remove

```
The .NET SDK also emits optional `id:` and `retry:` lines (see section 6). Defaults are omitted from the wire (mode outer, useViewTransition false).

### datastar-patch-signals
```
event: datastar-patch-signals
data: onlyIfMissing true
data: signals {foo: 1, bar: 2}

```
- `signals` line must be valid `data-signals` content (JSON works; JS-object literal also). JSON Merge Patch semantics: set a value to `null` to remove the signal (`data: signals {foo: null}`). `onlyIfMissing true` sets only signals that do not already exist (default false). Multiple lines can be sent as repeated `data: signals ...` (the .NET SDK splits on lines).

### Scripts and redirects
- Run a script: send a `<script>` inside patch-elements appended to the body (https://data-star.dev/guide/datastar_expressions#executing-scripts):
```
event: datastar-patch-elements
data: mode append
data: selector body
data: elements <script>window.location = "/guide"</script>

```
  (Redirect how-to: https://data-star.dev/how_tos/redirect_the_page_from_the_backend uses exactly this; the SDK helper `ExecuteScriptAsync("window.location = \"/guide\";")` wraps it.) Per ADR the SDK adds `data-effect="el.remove()"` to the script tag when autoRemove is true so the tag is cleaned up.
- Or respond with `Content-Type: text/javascript` and the code as the body (non-SSE).
- Note: a plain HTTP 302 redirect is NOT followed as a navigation by `fetch`-driven actions for the page; use the script approach (inference from how the how-to is written; see Not confirmed).

### Plain text/html (no SSE) response
Accepted (https://data-star.dev/reference/actions "Response Handling"; bundle routes `text/html` to the same patch-elements handler, reading mode/selector from the `datastar-*` response headers). Top-level elements are morphed by id exactly like an SSE patch-elements event with no options. Quote from the guide: "If the response has a content-type of text/html, the top-level HTML elements will be morphed into the existing DOM based on the element IDs."

---------------------------------------------------------------------
## 5. Morphing rules

Sources: https://data-star.dev/guide/getting_started#patching-elements, https://data-star.dev/reference/sse_events, https://data-star.dev/reference/attributes, ADR.
- Default patch = morph (idiomorph-style): top-level elements in `elements` are matched to existing DOM elements **by `id`**. "Be sure to place IDs on top-level elements to be morphed, as well as on elements within them that you'd like to preserve state on (event listeners, CSS transitions, etc.)." Without ids on top-level elements you must give `selector`.
- Morph preserves: existing attributes unless the new HTML explicitly changes them; element identity (so listeners/transitions survive); `data-*` attributes are only re-applied if the attribute itself changed. 1.0.5 fix: empty ids (`id=""`) no longer count as persistent.
- `data-ignore-morph` skips an element and its subtree; `data-preserve-attr="open class"` keeps listed attributes.
- Focus and typed input: the docs say morphing "ensures that only modified parts of the DOM are updated ... preserving state". I did not find a prose statement about focus/caret specifically (a string search of the bundle for `activeElement` found nothing). Treat as: keep an `id` on inputs that must keep focus/typed text, and prefer bound signals (`data-bind`) for input values so server patches do not clobber them. See Not confirmed.
- Append to a list: `mode append` + `selector #list` (example from https://data-star.dev/how_tos/load_more_list_items). Items appended this way are NOT morphed and not de-duplicated; give each appended item a unique id so a later outer-mode patch of that id updates it in place. Alternative ("fat morph"): re-send the whole `#list` container each time with mode outer (default), which is idempotent and recommended by the docs for resilience.
- Long-lived stream: an action's response may stay open; the server just keeps writing events and flushing, with delays between them. Docs example (https://data-star.dev/guide/getting_started): send patch, wait, send another. Progress bar example: `data-init="@get('/examples/progress_bar/updates', {openWhenHidden: true})"`, server sends a new SVG every 500 ms (https://data-star.dev/examples/progress_bar). For mission beats: `data-init="@get('/mission/stream', {openWhenHidden: true})"` and on the server loop `PatchElementsAsync(...)` + `await Task.Delay(...)`. The request cancels (and the SDK `RequestAborted` token fires) when the client navigates, calls another `@get` to the same URL (requestCancellation auto), or closes the stream.
- Docs recommendation for streams (https://data-star.dev/how_tos/prevent_sse_connections_closing): design for interruption; send the complete state of the main area each time ("fat morph") rather than incremental appends, because tab hiding or network drops can lose events.

---------------------------------------------------------------------
## 6. The .NET SDK

Sources: README and csproj/source at https://github.com/starfederation/datastar-dotnet (commit bec97852a2355415f7fa02d6abb6d8f222060bfe, "nuget 1.4"); NuGet flat container https://api.nuget.org/v3-flatcontainer/starfederation.datastar/index.json and the 1.4.0 nuspec.
- **Package id: `StarFederation.Datastar`** (C# surface). Depends on `StarFederation.Datastar.FSharp` (same version; F# core, pulled in transitively). **Latest on NuGet: 1.4.0.** Version history: 1.0.0 .. 1.1.x, 1.2.x, 1.3.0, 1.4.0. (Repo csproj `<Version>` still reads 1.2.0 but `<PackageVersion>` is 1.4; the published nuspec is 1.4.0.) I did not get a publish date for 1.4.0; repo commit is 2026-06-09.
- Target frameworks: **net8.0; net9.0; net10.0** (nuspec dependency groups for all three; framework reference `Microsoft.AspNetCore.App`). So .NET 10 is supported. The repo's global.json pins SDK 10.0.0. Targets Datastar SDK ADR; patch-elements/patch-signals names (1.0 protocol), `viewTransitionSelector` option present (matches 1.0.5).
- Registration and DI (namespace `StarFederation.Datastar.DependencyInjection`):
```csharp
using StarFederation.Datastar.DependencyInjection;
builder.Services.AddDatastar();            // returns IDatastarBuilder; registers scoped IDatastarService + IHttpContextAccessor
// optional: .AddJsonOptions(o => o.Converters.Add(new JsonStringEnumConverter()));
```
  Default signals JSON options = `JsonSerializerDefaults.Web` (camelCase property names, case-insensitive reads).
- `IDatastarService` methods (all have overloads taking `CancellationToken`; the token defaults to `HttpContext.RequestAborted`):
  - `Task StartServerEventStreamAsync()` (optional; sends 200 + `text/event-stream`, `Cache-Control: no-cache`, `Connection: keep-alive` on HTTP/1.1; **called automatically on first event**)
  - `Task PatchElementsAsync(string elements)` / `(string elements, PatchElementsOptions options)`
  - `Task RemoveElementAsync(string selector)` / `(selector, RemoveElementOptions)`
  - `Task PatchSignalsAsync<T>(T signals)` (serialises with System.Text.Json) / with `JsonSerializerOptions` and/or `PatchSignalsOptions`
  - `Task ExecuteScriptAsync(string script)` / `(script, ExecuteScriptOptions)`
  - `Task<string?> ReadSignalsAsync()` raw JSON; `Task<T?> ReadSignalsAsync<T>()` typed (null if no signals); `Stream GetSignalsStream()`
  - Options classes: `PatchElementsOptions { Selector, PatchMode (ElementPatchMode: Outer, Inner, Remove, Replace, Prepend, Append, Before, After), UseViewTransition, ViewTransitionSelector, Namespace (Html/Svg/MathMl), EventId, Retry }`; `PatchSignalsOptions { OnlyIfMissing, EventId, Retry }`; `RemoveElementOptions`; `ExecuteScriptOptions { EventId, Retry, AutoRemove, Attributes }`.
  - Gotcha seen in source: in `ExecuteScriptOptions`, `AutoRemove` defaults to `Consts.DefaultElementsUseViewTransitions` (= false), whereas the ADR says default true. So scripts are NOT auto-removed by default in the .NET SDK; pass `new ExecuteScriptOptions { AutoRemove = true }` if wanted.
  - Reads: for GET and DELETE the SDK reads query parameter `datastar`; all other methods read the body. Reading the body twice is not possible; call `ReadSignalsAsync<T>()` once.
- Model binding (MVC/minimal): `[FromSignals]` attribute (README "Model Binding" section; `Path = "a.b"` supported; only one `[FromSignals]` per action for body-signal methods). The README shows it in MVC controller style; I did not verify it in minimal APIs (the DI service approach is what the official docs show for minimal APIs).
- Minimal-API examples copied from the official docs (https://data-star.dev/guide/getting_started and /guide/backend_requests):
```csharp
using StarFederation.Datastar.DependencyInjection;

builder.Services.AddDatastar();

app.MapGet("/", async (IDatastarService datastarService) =>
{
    await datastarService.PatchElementsAsync(@"<div id=""hal"">I'm sorry, Dave. I'm afraid I can't do that.</div>");
    await Task.Delay(TimeSpan.FromSeconds(1));
    await datastarService.PatchElementsAsync(@"<div id=""hal"">Waiting for an order...</div>");
});

// reading typed signals
public record Signals
{
    [JsonPropertyName("foo")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FooSignals? Foo { get; set; } = null;
    public record FooSignals
    {
        [JsonPropertyName("bar")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Bar { get; set; }
    }
}
app.MapGet("/read-signals", async (IDatastarService datastarService) =>
{
    Signals? mySignals = await datastarService.ReadSignalsAsync<Signals>();
    var bar = mySignals?.Foo?.Bar;
});

// patch signals, multiple events per response
await datastarService.PatchSignalsAsync(new { response = "", answer = "bread" });
```
  Redirect helper (https://data-star.dev/how_tos/redirect_the_page_from_the_backend): `await datastarService.ExecuteScriptAsync("""window.location = "/guide";""");`
  Append example in options form (my composition from the SDK types, not from the docs): `await ds.PatchElementsAsync("<li id=\"b1\">x</li>", new PatchElementsOptions { Selector = "#beats", PatchMode = ElementPatchMode.Append });`
- F#-first? Yes: core is F# (`StarFederation.Datastar.FSharp`, static `ServerSentEventGenerator.PatchElementsAsync(HttpResponse, ...)` etc.), but the C# package wraps it with the DI service above; no F# knowledge needed from C#. `ElementPatchMode` type used in options is the C# enum in the `StarFederation.Datastar` namespace (the F# DU is converted internally).
- Skipping the SDK is practical: the protocol is plain text. In ASP.NET Core minimal API:
```csharp
app.MapGet("/stream", async (HttpResponse res, CancellationToken ct) =>
{
    res.Headers.ContentType = "text/event-stream";
    res.Headers.CacheControl = "no-cache";           // add "Connection: keep-alive" only on HTTP/1.1
    await res.StartAsync(ct);
    async Task Send(string html)
    {
        var sb = new StringBuilder("event: datastar-patch-elements\n");
        foreach (var line in html.ReplaceLineEndings("\n").Split('\n'))
            sb.Append("data: elements ").Append(line).Append('\n');
        sb.Append('\n');                               // blank line terminates the event
        await res.WriteAsync(sb.ToString(), ct);
        await res.Body.FlushAsync(ct);
    }
    await Send("<div id=\"beat\">Beat 1</div>");
    await Task.Delay(1000, ct);
    await Send("<div id=\"beat\">Beat 2</div>");
});
```
  Exact bytes for one event: `event: datastar-patch-elements\ndata: elements <div id="foo">Hello</div>\n\n`. Signals: `event: datastar-patch-signals\ndata: signals {"count":1}\n\n`. Remember to HTML-encode user text in the HTML you emit.

---------------------------------------------------------------------
## 7. Forms

Sources: https://data-star.dev/examples/form_data, https://data-star.dev/examples/inline_validation, https://data-star.dev/examples/click_to_edit, https://data-star.dev/reference/actions (options), https://data-star.dev/reference/attributes (data-bind).
Two supported approaches:
1. **`contentType: 'form'`** (closest to classic HTML): Datastar finds the form, validates with the browser, and sends `application/x-www-form-urlencoded` (or multipart if the form has `enctype="multipart/form-data"`; use for file uploads). **No signals are sent in this mode.** Names come from `name=` attributes, so checkboxes sharing one name arrive as repeated keys, radios as one value, selects as the chosen value; in ASP.NET read with `[FromForm]`/`Request.ReadFormAsync()` (the .NET SDK does not wrap form reading; DOCS do not show server code for it). Official markup:
```html
<form id="myform">
  foo:<input type="checkbox" name="checkboxes" value="foo" />
  bar:<input type="checkbox" name="checkboxes" value="bar" />
  <button data-on:click="@post('/endpoint', {contentType: 'form'})">Submit POST request</button>
</form>
<!-- or on submit -->
<form data-on:submit="@post('/endpoint', {contentType: 'form'})"> ... <button>Submit</button></form>
```
   (`data-on:submit` auto-prevents the browser's own submit.)
2. **Bound signals**: `data-bind:first-name` on each control, then `@post('/save')` with default JSON mode sends all non-underscore signals as a JSON body. Predefined signal types are preserved: `data-signals:n="0"` + `<select data-bind:n>` yields a number; `data-signals:tags="[]"` + several `<input type=checkbox data-bind:tags value="a">` yields an array of values; radios bind to one signal. Checkbox with a boolean signal binds true/false. The click-to-edit official example uses this style and says "There is no form ... the body is the entire contents of the signals", validation is server-side.
- Validation errors: the server returns a `datastar-patch-elements` that replaces the form (or just the message element next to a control), e.g. patch `#email-error` to `<p id="email-error" class="error">Invalid email</p>`. The inline-validation example sends a `@query(...)` (HTTP QUERY method) on `data-on:keydown__debounce.500ms` with bound signals and patches the whole demo block with validation results (https://data-star.dev/examples/inline_validation); equivalent with plain `@post`/`@get` works (QUERY is awkward in ASP.NET minimal APIs: needs `app.MapMethods("/x", new[] {"QUERY"}, handler)`).
- Browser validation: with `contentType:'form'`, HTML5 attributes (`required`, `type=email`, `pattern`) are checked before sending and `reportValidity()` shows native bubbles. Pro-only `data-custom-validity` exists; avoid.
- CSRF: send via `headers: {'X-Csrf-Token': '...'}` (docs example) or a cookie/antiforgery token in a hidden field with `contentType: 'form'`.

---------------------------------------------------------------------
## 8. Gotchas

- **Numbers**: signals are JSON-serialised JavaScript values, so numbers are IEEE-754 doubles. Integers above 2^53-1 lose precision when they pass through `JSON.parse` / the signal store (this follows from JS semantics; the Datastar docs do not state it - see Not confirmed). Send 64-bit ids (long, snowflake, Guid is fine) as **strings**, in signals and in patch-signals JSON. The .NET `ReadSignalsAsync<T>` is System.Text.Json so a `long` property would parse a JSON number but precision was already lost client-side. Also: element `value` is a string; Datastar casts a bound signal to its predefined type (number if the signal was predefined as a number).
- **Signal names**: camelCase after conversion (`data-signals:my-signal` -> `$mySignal`); nested with dots; no `__` anywhere in the name; `$` prefix in expressions. Setting a signal to null/undefined removes it. Server property names should match: .NET default signals JSON = camelCase.
- **Underscore = local**: signals whose name starts with `_` (or any path segment starts with `_`) are NOT sent to the backend by default (`filterSignals` default exclude). Also `data-indicator:_fetching` is the idiom used in official examples. Do not treat them as secret; they are still visible in the page. Signals are client-modifiable; always validate on the server (https://data-star.dev/reference/security).
- **CSP**: default mode compiles expressions with `Function()` so the CSP must allow **`'unsafe-eval'`** (`script-src 'self' 'unsafe-eval'`). v1.0.5 also has a **CSP mode** that avoids it: put `data-nonce="{page-nonce}"` on `<html>`, CSP `script-src 'self' 'nonce-{page-nonce}'`, fresh random nonce per full-page response (https://data-star.dev/reference/security#content-security-policy). Trusted Types policy named `datastar` is supported. CSP mode does not sanitise anything: never interpolate untrusted text into attributes; use signals and HTML-encode.
- **Hidden tab**: for GET the stream is closed when the page is hidden and reopened when visible (default `openWhenHidden:false` for GET; true for POST/PUT/PATCH/DELETE/QUERY). Use `{openWhenHidden: true}` for dashboards/long streams at the cost of battery. The bundle listens to `visibilitychange` (fixed in 1.0.5: duplicate connections after visibility change, #1213 and #1230).
- **Reconnect**: `retry` option default `'auto'` = retry on network errors only, with `retryInterval` 1000 ms, scaler 2, max wait 30 s, max 10 attempts; HTTP 4xx/5xx are NOT retried unless `retry:'error'`/`'always'`; `retry:'never'` stops after network/stream failures (1.0.5 fix #1234). SSE `id:`/`retry:` lines from the server are honoured (bundle tracks `last-event-id` and sends header `last-event-id` on reconnect). A reconnect re-issues the same request, so make long streams idempotent and prefer sending full state (fat morph). A response that is not `text/event-stream`, html, json, or js raises error `FetchExpectedTextEventStream`.
- Request cancellation: a new `@get` to the same URL with same method aborts the previous one (default `requestCancellation:'auto'`), so repeated clicks on a stream-starting button restart it.
- **One connection per origin on HTTP/1.1**: browsers cap ~6 per host; keep Kestrel on HTTP/2 (TLS) for multiple open streams. (General SSE knowledge, not from Datastar docs.)
- **Pro-only (paid) - avoid**: attributes `data-animate`, `data-custom-validity`, `data-match-media`, `data-on-raf`, `data-on-resize`, `data-persist`, `data-query-string`, `data-replace-url`, `data-scroll-into-view`, `data-view-transition`; actions `@clipboard()`, `@fit()`, `@intl()` (https://data-star.dev/reference/attributes "Pro Attributes", https://data-star.dev/reference/actions "Pro Actions"). All other attributes/actions listed in sections 2 and 3 are in the free MIT bundle (the free bundle I downloaded is the public `bundles/datastar.js`).
- Expression gotchas: need `;` between statements; attributes processed in source order; HTML5 attribute names are lowercased by the parser (hence hyphens).
- Morph: elements without ids cannot be matched; `mode append` items accumulate (not idempotent); `replace` mode resets state.
- Datastar sends the whole signal object on every request by default (docs: "not recommended to send partial signals"); keep large data out of signals or exclude with `filterSignals`.
- ASP.NET: Datastar IDatastarService is scoped per request; only use it inside the request. Always pass/propagate the `RequestAborted` token (the SDK default) so loops end when the client disconnects. Response compression middleware and proxies that buffer will break streaming; flush after each event (SDK does).

---------------------------------------------------------------------
## 9. Official-site examples (closest matches)

The example pages show the front-end HTML; their back-end code is in per-language tabs that my text fetch did not return, so the server side below uses the documented SDK calls (from the guide pages) and is labelled.

### (a) Button posts and replaces part of the page
Front end - official (https://data-star.dev/guide/getting_started):
```html
<button data-on:click="@get('/endpoint')">Open the pod bay doors, HAL.</button>
<div id="hal"></div>
```
Server, official C# (same page):
```csharp
app.MapGet("/endpoint", async (IDatastarService ds) =>
    await ds.PatchElementsAsync(@"<div id=""hal"">I'm sorry, Dave. I'm afraid I can't do that.</div>"));
```
Quiz variant with signals + POST (https://data-star.dev/guide/backend_requests): `<button data-on:click="@post('/actions/quiz')">Submit answer</button>`; server sends `PatchElementsAsync` for `#question` and `PatchSignalsAsync(new { response = "", answer = "bread" })` in one response. Click-to-edit with indicator (official, https://data-star.dev/examples/click_to_edit):
```html
<div id="demo">
  <p>First Name: John</p>
  <button data-indicator:_fetching data-attr:disabled="$_fetching"
          data-on:click="@get('/examples/click_to_edit/edit')">Edit</button>
</div>
```

### (b) Server streams several updates over time into a list
Front end official pieces: progress bar `<div id="progress-bar" data-init="@get('/examples/progress_bar/updates', {openWhenHidden: true})">` (https://data-star.dev/examples/progress_bar: "server sends down a new progress bar svg every 500 milliseconds") and the append list how-to (https://data-star.dev/how_tos/load_more_list_items):
```html
<div id="list"><div>Item 1</div></div>
<button id="load-more" data-signals:offset="1" data-on:click="@get('/how_tos/load_more/data')">Click to load another item</button>
```
Wire events the server sends there (official):
```
event: datastar-patch-elements
data: selector #list
data: mode append
data: elements <div>Item 2</div>

event: datastar-patch-signals
data: signals {offset: 2}

event: datastar-patch-elements
data: selector #load-more
data: mode remove

```
Streaming server loop, official C# stream shape (https://github.com/starfederation/datastar-dotnet examples/csharp/HelloWorld/Program.cs, the `stream-element-patches` endpoint):
```csharp
for (var index = 1; index < message.Length; ++index)
{
    await datastarService.PatchElementsAsync($"""<div id="message">{message[..index]}</div>""");
    await Task.Delay(TimeSpan.FromMilliseconds(delayMs));
}
```
Adapted for beats (my composition): client `<ol id="beats" data-init="@get('/mission/beats', {openWhenHidden: true})"></ol>`; server per beat `PatchElementsAsync($"<li id=\"beat-{n}\">{WebUtility.HtmlEncode(text)}</li>", new PatchElementsOptions { Selector = "#beats", PatchMode = ElementPatchMode.Append })` then `await Task.Delay(...)`.

### (c) Form with server validation
Official front end (https://data-star.dev/examples/inline_validation), bound signals, debounced keydown validation, click to submit:
```html
<div id="demo">
  <label>Email Address
    <input type="email" required aria-live="polite" aria-describedby="email-info"
           data-bind:email
           data-on:keydown__debounce.500ms="@query('/examples/inline_validation/validate')" />
  </label>
  <p id="email-info" class="info">The only valid email address is "[email protected]".</p>
  <label>First Name
    <input type="text" required data-bind:first-name
           data-on:keydown__debounce.500ms="@query('/examples/inline_validation/validate')" />
  </label>
  <button class="success" data-on:click="@post('/examples/inline_validation')">Sign Up</button>
</div>
```
Official form-submit markup (https://data-star.dev/examples/form_data): `<form data-on:submit="@post('/endpoint', {contentType: 'form'})"> foo: <input type="text" name="foo" required /> <button>Submit form</button></form>`.
Server side (my composition, uses documented SDK calls): on invalid input respond `await ds.PatchElementsAsync("<p id=\"email-error\" class=\"error\">Enter a valid email</p>");`; on success patch the form container (same id) with the success view.

---------------------------------------------------------------------
## Not confirmed from a primary source

- Datastar docs do not state that 64-bit integers lose precision; this is inferred from JSON/JavaScript number semantics (JSON.parse into doubles). Safe rule: send big ids as strings.
- Exact focus / caret / typed-text preservation rules during morph: docs only say morph "preserves state"; I did not locate the idiom (idiomorph) behaviour description or read the minified morph code for focus handling. Test with a real input before relying on it.
- Whether a server HTTP 302 on a `@get/@post` is ever treated as navigation (bundle shows redirect statuses 300-399 are not retried and the request is ended; no navigation). Use the script/redirect-through-patch approach instead.
- Per-example server-side source for the official examples (inline validation, click to load, progress bar): not retrieved; only front-end HTML and explanation text were read. Server snippets in section 9 are labelled composition where not official.
- NuGet 1.4.0 publish date; the SDK's `[FromSignals]` use in minimal APIs (only MVC-style shown in README); whether the SDK serialises `PatchSignalsAsync` JSON with camelCase for anonymous objects (it uses `JsonSerializerDefaults.Web` per source, so yes by source reading; the docs example `new { response = "", answer = "bread" }` is already lowercase).
- Whether `Datastar-Request: true` header can be relied on server-side for full-page versus fragment decisions: sent by the bundle (seen in source) and documented as default behaviour, but no server-side recipe was checked.
- Rocket (web component layer) and the Datastar Inspector were intentionally not researched.
