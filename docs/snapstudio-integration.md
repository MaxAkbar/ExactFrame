# Capture a frame in SnapStudio

Position a region with ExactFrame, then send a still capture of it to [SnapStudio](https://github.com/MaxAkbar/SnapStudio) for annotation, copy, or export.

## Before you start

Build integration-enabled versions of **both** projects from source. The published ExactFrame **2.1.1** and SnapStudio **1.1.0** packages do not include this connection. Keep the complete SnapStudio output folder, including `SnapStudio.integration.json`. Finish SnapStudio's first-run setup and leave still capture enabled. Close older running copies and run both apps with matching permissions.

## Capture

1. In **Outline area**, position the main frame, or add an additional guide.
2. Under **Capture in SnapStudio**, choose **Main frame** or an additional guide from **Capture area**.
3. Click **Capture in SnapStudio**. On first use, choose **SnapStudio.App.exe** from the integration-enabled build. The path is remembered; **Choose app…** changes it later.
4. Annotate the new document in SnapStudio, then copy or export it.

ExactFrame starts SnapStudio if necessary, then hides its own windows, floating bar, and overlays. SnapStudio minimizes during capture and brings the resulting document forward. ExactFrame restores its windows after success, failure, cancellation, or disconnection without taking focus from SnapStudio. The clipboard stays unchanged.

Dimensions are **physical screen pixels**, regardless of Windows scaling. An additional guide captures its actual on-screen crop; it does not upscale to the output size shown in composition controls. Negative display coordinates are supported. The entire frame must fit on one connected display, with each side at least 4 pixels and at most 20,000 pixels, and no more than 40 million pixels total. Reposition the frame after changing display settings.

Finish any SnapStudio dialog or edit before capturing. Wait for an active capture or recording to finish. If startup takes too long, open SnapStudio manually, finish setup, and retry. An older build selected through **Choose app…** produces a compatibility message.

## Local connection

The apps exchange frame bounds and status over a current-user-only, session-specific Windows named pipe. SnapStudio captures the screen and stores the document in its own library; images and databases are not shared between the apps. This integration makes no online requests.

See the [SnapStudio protocol documentation](https://github.com/MaxAkbar/SnapStudio/blob/master/docs/exactframe-integration.md) for the v1 message format and timeouts. For isolated development checks, use separate `EXACTFRAME_DATA_ROOT` and `SNAPSTUDIO_DATA_ROOT` directories and the same unique `SNAPSTUDIO_INTEGRATION_PIPE` value in both processes. ExactFrame remains single-instance within a Windows session.

Local Windows checks captured a 640 × 360 main frame and a 189 × 336 nested guide at 175% display scaling, verified exact image sizes and known pixels, restored the outline, and kept SnapStudio in front after success. A capture attempted during SnapStudio's New image dialog returned an error and restored ExactFrame without adding an image. The first-use file picker opened; choosing a path and saving it were tested through the service abstraction. Native checks on other monitor layouts, negative-coordinate displays, ARM64, and MSIX builds remain outstanding.
