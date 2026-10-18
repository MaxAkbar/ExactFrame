using System.Drawing;

namespace ExactFrame.Core.Obs;

public static class ObsFrameMapper
{
    public static ObsPlan Create(ObsSnapshot source, ObsRegion region, ObsPlacement placement)
    {
        var display = source.DisplayBounds;
        var frame = region.Bounds;
        var video = source.Video;
        if (frame.Width <= 0 || frame.Height <= 0 || !display.Contains(frame))
            throw new ObsException("This frame is outside the display captured by the selected OBS source. Choose a source for that display.");
        if (source.Transform.Number("sourceWidth") != display.Width || source.Transform.Number("sourceHeight") != display.Height)
            throw new ObsException("The OBS source size does not match its display. Make the source visible in OBS, then refresh the preview.");
        if (video.CanvasWidth <= 0 || video.CanvasHeight <= 0 || video.OutputWidth <= 0 || video.OutputHeight <= 0)
            throw new ObsException("OBS has no usable video dimensions.");
        if (!Enum.IsDefined(placement)) throw new ArgumentOutOfRangeException(nameof(placement));
        double scale = placement == ObsPlacement.FitToCanvas
            ? Math.Min((double)video.CanvasWidth / frame.Width, (double)video.CanvasHeight / frame.Height) : 1;
        if (placement == ObsPlacement.ActualPixels && (frame.Width > video.CanvasWidth || frame.Height > video.CanvasHeight))
            throw new ObsException("The frame is larger than the OBS canvas. Choose Fit to canvas, or a smaller frame.");
        // OBS crop coordinates are source pixels. Windows display scaling must not be applied here.
        int left = frame.Left - display.Left, top = frame.Top - display.Top;
        return new ObsPlan(source, region, placement, left, top,
            display.Width - left - frame.Width, display.Height - top - frame.Height, scale,
            SnapPosition((video.CanvasWidth - frame.Width * scale) / 2), SnapPosition((video.CanvasHeight - frame.Height * scale) / 2));
    }

    // OBS quantizes absolute scene positions to half pixels (libobs/obs-scene.c: item_absolute_pos).
    private static double SnapPosition(double value) => Math.Floor(value * 2 + 0.5) / 2;
}
