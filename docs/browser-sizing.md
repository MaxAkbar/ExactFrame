# Browser sizing and verification

ExactFrame sizes recording regions in **physical screen pixels**. This applies to the outline, Whole window, Client area and Web page modes, presets, profiles and remembered sizes. Web page mode uses Windows measurements of the visible page capture area; it does not query the page's DOM. The labels and verification guidance described here are included in ExactFrame 2.1.0.

## Keep the units separate

| Value | Meaning and source |
| --- | --- |
| ExactFrame physical dimensions | Requested capture size, followed by the size Windows reports after a resize. Check recorder scaling separately. |
| Windows logical size | Approximate requested size divided by the selected display's Windows scale. Rounded for display; this is not a CSS or browser zoom measurement. |
| CSS layout viewport | The page's `window.innerWidth` and `window.innerHeight`, reported by the browser in CSS pixels. These include scrollbar space. |
| Content viewport | `document.documentElement.clientWidth` and `clientHeight` exclude scrollbar space in a standards-mode document. They can differ from the layout viewport. |
| Device-pixel ratio (DPR) | The browser's `window.devicePixelRatio`. Page zoom and the display can affect it. It is not a browser zoom percentage by itself. |
| Visual viewport | `window.visualViewport` dimensions and scale describe the visible part of the page. Its scale can reflect pinch zoom; it is not the browser menu's page zoom value. |
| Browser page zoom | Read the percentage from the browser menu and record it separately. ExactFrame does not read or change it. |

**ExactFrame does not measure CSS viewport dimensions, browser page zoom or DPR.** The app's Windows logical-size note must not be used as a substitute.

For illustration, a 1920 × 1080 physical area corresponds to an estimated 1280 × 720 CSS area at a browser-reported DPR of 1.5, or 960 × 540 at DPR 2. A Windows logical-size conversion alone cannot tell you which CSS dimensions the page actually reports. Chrome documents the [distinction between physical pixels and CSS pixels](https://developer.chrome.com/docs/devtools/device-mode); Mozilla documents [DPR changes with page zoom and display changes](https://developer.mozilla.org/en-US/docs/Web/API/Window/devicePixelRatio).

## Check a browser setup

1. Choose the physical capture size and measurement mode in ExactFrame. **Client area** can include browser toolbars; **Web page** excludes measurable browser chrome. Compare the requested size with the accepted physical result after resizing.
2. Note the browser and version, Windows display scale, and page zoom from the browser menu. For a desktop check, turn off device emulation and avoid pinch zoom. If testing emulation, record its viewport and DPR settings separately; emulation is an approximation.
3. On your own test page, use the browser's DevTools Console to read the values below. Undock DevTools or account for the space it takes: docking a panel can change the viewport. The existing [local recording test pattern](../tests/ExactFrame.WindowsChecks/fixture/index.html) also displays browser-reported CSS dimensions and DPR.
4. Record these values beside ExactFrame's physical dimensions. Repeat after changing page zoom, display, panels, tabs or emulation. Scrollbars can explain differences between layout and content viewport measurements.
5. Inspect a saved recording separately for output dimensions, scaling, crop and excluded overlays. Browser metrics do not verify recorder output.

This read-only snippet reports the current top-level page when run in its DevTools context. It makes no network or clipboard requests:

```javascript
(() => {
  const viewport = window.visualViewport;
  return {
    layoutViewportCss: { width: window.innerWidth, height: window.innerHeight },
    contentViewportCss: {
      width: document.documentElement.clientWidth,
      height: document.documentElement.clientHeight
    },
    devicePixelRatio: window.devicePixelRatio,
    visualViewport: viewport ? {
      widthCss: viewport.width, heightCss: viewport.height, scale: viewport.scale
    } : null,
    screenSpanEstimatePx: {
      width: Math.round(window.innerWidth * window.devicePixelRatio),
      height: Math.round(window.innerHeight * window.devicePixelRatio)
    },
    pageZoom: "Read separately from the browser menu"
  };
})()
```

The CSS × DPR calculation is an **estimate for a normal desktop page**, not a native screen rectangle. Rounding, scrollbars, pinch scaling and device emulation can make a direct comparison inappropriate. A CSS `1px` line can occupy more than one physical screen pixel. Consult the browser's [layout viewport](https://developer.mozilla.org/en-US/docs/Web/API/Window/innerWidth), [content viewport](https://developer.mozilla.org/en-US/docs/Web/API/Element/clientWidth), and [visual viewport scale](https://developer.mozilla.org/en-US/docs/Web/API/VisualViewport/scale) documentation when interpreting the report.

## Scope and evidence

This step clarifies labels, accessibility names, setup reports and Help. It does not change resizing, unit storage or browser settings. The test pattern's browser values are independent QA observations; they are not connected to the product's selected window. No browser extension, remote-debugging connection, automatic CSS resizing or inferred page-zoom percentage is added. Live browser integration remains deferred until developer demand is validated.

The Windows browser harness can compare the production page rectangle with a fresh test-pattern CSS × DPR estimate. See [its opt-in instructions](../tests/ExactFrame.WindowsChecks/README.md). That comparison is limited to its named browser, zoom, display and fixture; passing Core tests does not establish CSS sizing accuracy in other browsers or recorder output.

### Checks on 2026-10-18

- All 420 existing Core tests passed. The Release x64 app built with no warnings or errors. Existing expected-label assertions were updated; no new resizing or conversion behavior was introduced.
- Native Windows review confirmed the physical-pixel accessibility names, the rounded Windows logical-size explanation at 150% scaling, the not-measured browser status, and readable wrapping in the Resize window panel and Help guide. No app window was resized by these checks.
- The guide's read-only snippet ran on the public Chrome Device Mode documentation page in the in-app browser. It returned a 1280 × 720 CSS layout viewport, 1265 × 720 content viewport, DPR 1.5 and an estimated 1920 × 1080 screen span. Page zoom remained a separate manual reading. These observations test the snippet, not ExactFrame browser integration or a native screen rectangle.
- Both JavaScript blocks passed syntax checks. The browser tool's file-URL policy blocked loading the local recording fixture, so the revised fixture wording was reviewed in source and was not exercised in that browser. Recorder and mixed-DPI browser-resize checks were not rerun.

Local receipts are under ignored `artifacts/browser-sizing/20261018-clarity/`; no captured screen images are retained there.
