using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using ExactFrame.Core.Geometry;

namespace ExactFrame.Native;

/// <summary>A click-through pixel loupe shown beside whichever outline edge the pointer is inspecting.</summary>
internal sealed class EdgeMagnifierWindow : LayeredWindow
{
    private const int SamplePixels = 31;
    private const int Zoom = 8;
    private const int Padding = 12;
    private const int ImageTop = 32;
    private const int Gap = 32;
    private static readonly Size LensSize = new(Padding * 2 + SamplePixels * Zoom, 300);

    public EdgeMagnifierWindow() : base(clickThrough: true)
    {
    }

    public void Update(Rectangle frame, Point pointer, FrameEdge edge, Rectangle display)
    {
        using var sample = new Bitmap(SamplePixels, SamplePixels, PixelFormat.Format32bppArgb);
        using (var capture = Graphics.FromImage(sample))
            capture.CopyFromScreen(pointer.X - SamplePixels / 2, pointer.Y - SamplePixels / 2,
                0, 0, sample.Size, CopyPixelOperation.SourceCopy);

        var bounds = EdgeMagnifierGeometry.PlaceLens(pointer, LensSize, display, Gap);
        Render(bounds, graphics => Draw(graphics, sample, frame, pointer, edge));
        if (!IsShown) Show();
    }

    private static void Draw(Graphics graphics, Bitmap sample, Rectangle frame, Point pointer, FrameEdge edge)
    {
        graphics.SmoothingMode = SmoothingMode.None;
        using var background = new SolidBrush(Color.FromArgb(246, 15, 31, 36));
        using var soft = new SolidBrush(Color.FromArgb(255, 190, 211, 211));
        using var white = new SolidBrush(Color.White);
        using var accent = new Pen(Color.FromArgb(255, 52, 213, 181), 2);
        using var border = new Pen(Color.FromArgb(255, 52, 213, 181), 1);
        using var font = new Font("Segoe UI", 15, FontStyle.Bold, GraphicsUnit.Pixel);
        using var small = new Font("Consolas", 13, FontStyle.Regular, GraphicsUnit.Pixel);

        graphics.FillRectangle(background, 0, 0, LensSize.Width, LensSize.Height);
        graphics.DrawRectangle(border, 0, 0, LensSize.Width - 1, LensSize.Height - 1);
        graphics.DrawString($"{edge.ToString().ToUpperInvariant()} EDGE  ·  8×", font, white, Padding, 10);

        var image = new Rectangle(Padding, ImageTop, SamplePixels * Zoom, SamplePixels * Zoom);
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.DrawImage(sample, image, new Rectangle(0, 0, SamplePixels, SamplePixels), GraphicsUnit.Pixel);

        using (var grid = new Pen(Color.FromArgb(45, 255, 255, 255), 1))
        {
            for (int i = 1; i < SamplePixels; i++)
            {
                int line = i * Zoom;
                graphics.DrawLine(grid, image.Left + line, image.Top, image.Left + line, image.Bottom);
                graphics.DrawLine(grid, image.Left, image.Top + line, image.Right, image.Top + line);
            }
        }

        int center = SamplePixels / 2 * Zoom + Zoom / 2;
        using (var crosshair = new Pen(Color.FromArgb(245, 255, 255, 255), 1))
        {
            graphics.DrawLine(crosshair, image.Left + center, image.Top, image.Left + center, image.Bottom);
            graphics.DrawLine(crosshair, image.Left, image.Top + center, image.Right, image.Top + center);
        }

        int boundary = edge switch
        {
            FrameEdge.Left => frame.Left - pointer.X,
            FrameEdge.Right => frame.Right - 1 - pointer.X,
            FrameEdge.Top => frame.Top - pointer.Y,
            _ => frame.Bottom - 1 - pointer.Y
        };
        int position = (SamplePixels / 2 + boundary) * Zoom + Zoom / 2;
        if (edge is FrameEdge.Left or FrameEdge.Right)
            graphics.DrawLine(accent, image.Left + position, image.Top, image.Left + position, image.Bottom);
        else
            graphics.DrawLine(accent, image.Left, image.Top + position, image.Right, image.Top + position);

        graphics.DrawString($"X {pointer.X}   Y {pointer.Y}", small, soft, Padding, 284);
    }
}
