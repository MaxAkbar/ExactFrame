using System.Drawing;
using System.Drawing.Imaging;
using ExactFrame.Interop;

namespace ExactFrame.Native;

/// <summary>
/// A topmost, non-activating window drawn with per-pixel alpha. Pixels with zero alpha are
/// transparent to the mouse, so only what is drawn can be clicked (unless the window is click-through).
/// </summary>
internal abstract class LayeredWindow : NativeWindow
{
    private Rectangle _bounds;

    protected LayeredWindow(bool clickThrough)
    {
        int exStyle = NativeMethods.WsExLayered | NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate |
                      NativeMethods.WsExTopmost | (clickThrough ? NativeMethods.WsExTransparent : 0);
        CreateHandle(exStyle, NativeMethods.WsPopup);
    }

    public Rectangle Bounds => _bounds;

    public bool IsShown { get; private set; }

    /// <summary>Draws the whole window. The graphics origin is the window's top-left corner.</summary>
    protected void Render(Rectangle bounds, Action<Graphics> draw)
    {
        _bounds = bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        nint screenDc = NativeMethods.GetDC(0);
        nint memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
        var header = new NativeMethods.BitmapInfoHeader
        {
            Size = 40,
            Width = bounds.Width,
            Height = -bounds.Height, // top-down
            Planes = 1,
            BitCount = 32
        };
        nint dib = NativeMethods.CreateDIBSection(memoryDc, ref header, 0, out nint bits, 0, 0);
        try
        {
            NativeMethods.Check(dib != 0, "allocate the overlay bitmap");
            nint previous = NativeMethods.SelectObject(memoryDc, dib);
            try
            {
                using (var bitmap = new Bitmap(bounds.Width, bounds.Height, bounds.Width * 4, PixelFormat.Format32bppPArgb, bits))
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Transparent);
                    draw(graphics);
                    graphics.Flush();
                }

                var destination = new NativeMethods.Point(bounds.X, bounds.Y);
                var size = new NativeMethods.Size(bounds.Width, bounds.Height);
                var source = new NativeMethods.Point(0, 0);
                var blend = new NativeMethods.BlendFunction
                {
                    BlendOp = NativeMethods.AcSrcOver,
                    SourceConstantAlpha = 255,
                    AlphaFormat = NativeMethods.AcSrcAlpha
                };
                NativeMethods.Check(NativeMethods.UpdateLayeredWindow(Handle, screenDc, ref destination, ref size,
                    memoryDc, ref source, 0, ref blend, NativeMethods.UlwAlpha), "draw the outline");
            }
            finally
            {
                NativeMethods.SelectObject(memoryDc, previous);
            }
        }
        finally
        {
            if (dib != 0) NativeMethods.DeleteObject(dib);
            NativeMethods.DeleteDC(memoryDc);
            NativeMethods.ReleaseDC(0, screenDc);
        }
    }

    /// <summary>Moves without redrawing; the drawn bitmap travels with the window.</summary>
    public void MoveTo(Point location)
    {
        _bounds = new Rectangle(location, _bounds.Size);
        NativeMethods.SetWindowPos(Handle, 0, location.X, location.Y, 0, 0,
            NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
    }

    public void Show()
    {
        NativeMethods.ShowWindow(Handle, NativeMethods.SwShowNoActivate);
        NativeMethods.SetWindowPos(Handle, NativeMethods.HwndTopmost, 0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
        IsShown = true;
    }

    public void Hide()
    {
        NativeMethods.ShowWindow(Handle, NativeMethods.SwHide);
        IsShown = false;
    }

    public bool SetCaptureExclusion(bool exclude) =>
        NativeMethods.SetWindowDisplayAffinity(Handle, exclude ? NativeMethods.WdaExcludeFromCapture : NativeMethods.WdaNone);

    protected void SetClickThrough(bool clickThrough)
    {
        long style = NativeMethods.GetWindowLongPtr(Handle, NativeMethods.GwlExStyle).ToInt64();
        style = clickThrough ? style | NativeMethods.WsExTransparent : style & ~NativeMethods.WsExTransparent;
        NativeMethods.SetWindowLongPtr(Handle, NativeMethods.GwlExStyle, (nint)style);
    }

    protected override nint WndProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case NativeMethods.WmMouseActivate:
                return NativeMethods.MaNoActivate;
            case NativeMethods.WmDpiChanged:
                // Keep exact physical-pixel geometry; never adopt Windows' suggested scaled rectangle.
                return 0;
        }
        return base.WndProc(hwnd, message, wParam, lParam);
    }
}
