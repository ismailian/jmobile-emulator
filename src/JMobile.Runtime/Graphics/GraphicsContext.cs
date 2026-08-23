using System.Runtime.CompilerServices;

namespace JMobile.Runtime.Graphics;

/// <summary>
/// The software rasteriser behind <c>javax.microedition.lcdui.Graphics</c>.
/// </summary>
/// <remarks>
/// Every drawing operation a MIDlet performs ends up here, so this is the second hot path of
/// the emulator after the interpreter. Three things keep it fast:
/// <list type="bullet">
/// <item>Clipping is resolved once per call into an absolute rectangle, and each primitive
/// then works in already-clipped device coordinates. No inner loop tests the clip.</item>
/// <item>Horizontal runs are filled with <see cref="Span{T}.Fill"/> and copied with
/// <see cref="Span{T}.CopyTo"/>, which vectorise, instead of being written pixel by pixel.</item>
/// <item>Opaque blits skip blending entirely. Games composite tile layers constantly and most
/// of those tiles have no translucency at all.</item>
/// </list>
/// </remarks>
public sealed class GraphicsContext
{
    private Surface _target;

    // The clip is stored in absolute device coordinates as a half-open rectangle, because that
    // is the form every primitive needs; the MIDP-visible accessors convert back on demand.
    private int _clipLeft;
    private int _clipTop;
    private int _clipRight;
    private int _clipBottom;

    public GraphicsContext(Surface target)
    {
        _target = target;
        ResetTo(target);
    }

    public Surface Target => _target;

    /// <summary>Current drawing colour, always opaque, as MIDP defines it.</summary>
    public int Color { get; private set; } = PixelBlend.OpaqueMask;

    public int TranslateX { get; private set; }

    public int TranslateY { get; private set; }

    public BitmapFont? Font { get; set; }

    public int Stroke { get; set; } = StrokeStyle.Solid;

    /// <summary>
    /// Rebinds this context to another surface and resets its state.
    /// </summary>
    /// <remarks>
    /// Contexts are reused rather than reallocated. A game may call <c>getGraphics</c> on an
    /// off-screen image inside its render loop, and allocating a context per frame would put
    /// avoidable pressure on the collector during exactly the frames that must not stutter.
    /// </remarks>
    public void ResetTo(Surface target)
    {
        _target = target;
        TranslateX = 0;
        TranslateY = 0;
        _clipLeft = 0;
        _clipTop = 0;
        _clipRight = target.Width;
        _clipBottom = target.Height;
        Color = PixelBlend.OpaqueMask;
        Stroke = StrokeStyle.Solid;
    }

    // ---------------------------------------------------------------------------------
    // State
    // ---------------------------------------------------------------------------------

    public void SetColor(int rgb) => Color = PixelBlend.Opaque(rgb);

    public void SetColor(int red, int green, int blue) => Color = PixelBlend.Pack(red, green, blue);

    public void SetGrayScale(int value) => Color = PixelBlend.Pack(value, value, value);

    public void Translate(int dx, int dy)
    {
        TranslateX += dx;
        TranslateY += dy;
    }

    public int ClipX => _clipLeft - TranslateX;

    public int ClipY => _clipTop - TranslateY;

    public int ClipWidth => Math.Max(0, _clipRight - _clipLeft);

    public int ClipHeight => Math.Max(0, _clipBottom - _clipTop);

    public void SetClip(int x, int y, int width, int height)
    {
        int left = x + TranslateX;
        int top = y + TranslateY;

        _clipLeft = Math.Max(0, left);
        _clipTop = Math.Max(0, top);
        _clipRight = Math.Min(_target.Width, left + width);
        _clipBottom = Math.Min(_target.Height, top + height);
    }

    /// <summary>Intersects the clip with a further rectangle, as <c>clipRect</c> does.</summary>
    public void ClipRect(int x, int y, int width, int height)
    {
        int left = x + TranslateX;
        int top = y + TranslateY;

        _clipLeft = Math.Max(_clipLeft, left);
        _clipTop = Math.Max(_clipTop, top);
        _clipRight = Math.Min(_clipRight, left + width);
        _clipBottom = Math.Min(_clipBottom, top + height);
    }

    private bool ClipIsEmpty => _clipLeft >= _clipRight || _clipTop >= _clipBottom;

    // ---------------------------------------------------------------------------------
    // Rectangles
    // ---------------------------------------------------------------------------------

    public void FillRect(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0 || ClipIsEmpty) return;

        int left = Math.Max(_clipLeft, x + TranslateX);
        int top = Math.Max(_clipTop, y + TranslateY);
        int right = Math.Min(_clipRight, x + TranslateX + width);
        int bottom = Math.Min(_clipBottom, y + TranslateY + height);
        if (left >= right || top >= bottom) return;

        int color = Color;
        int stride = _target.Width;
        int[] pixels = _target.Pixels;
        int span = right - left;

        for (int row = top; row < bottom; row++)
            pixels.AsSpan((row * stride) + left, span).Fill(color);
    }

    public void DrawRect(int x, int y, int width, int height)
    {
        if (width < 0 || height < 0) return;

        // MIDP defines drawRect as spanning width+1 by height+1 pixels.
        DrawHorizontalLine(x, x + width, y);
        DrawHorizontalLine(x, x + width, y + height);
        DrawVerticalLine(x, y, y + height);
        DrawVerticalLine(x + width, y, y + height);
    }

    public void FillRoundRect(int x, int y, int width, int height, int arcWidth, int arcHeight)
    {
        if (width <= 0 || height <= 0) return;

        int radiusX = Math.Min(Math.Abs(arcWidth) / 2, width / 2);
        int radiusY = Math.Min(Math.Abs(arcHeight) / 2, height / 2);

        if (radiusX <= 0 || radiusY <= 0)
        {
            FillRect(x, y, width, height);
            return;
        }

        // The straight middle band is one fill; only the two capped bands need per-row insets.
        FillRect(x, y + radiusY, width, height - (2 * radiusY));

        for (int row = 0; row < radiusY; row++)
        {
            // How far the corner ellipse has come in at this distance above the straight band.
            double normalised = (double)(radiusY - row) / radiusY;
            int inset = radiusX - (int)Math.Round(radiusX * Math.Sqrt(Math.Max(0.0, 1.0 - Square(normalised))));

            FillRect(x + inset, y + row, width - (2 * inset), 1);
            FillRect(x + inset, y + height - 1 - row, width - (2 * inset), 1);
        }
    }

    public void DrawRoundRect(int x, int y, int width, int height, int arcWidth, int arcHeight)
    {
        if (width < 0 || height < 0) return;

        int radiusX = Math.Min(Math.Abs(arcWidth) / 2, width / 2);
        int radiusY = Math.Min(Math.Abs(arcHeight) / 2, height / 2);

        if (radiusX <= 0 || radiusY <= 0)
        {
            DrawRect(x, y, width, height);
            return;
        }

        DrawHorizontalLine(x + radiusX, x + width - radiusX, y);
        DrawHorizontalLine(x + radiusX, x + width - radiusX, y + height);
        DrawVerticalLine(x, y + radiusY, y + height - radiusY);
        DrawVerticalLine(x + width, y + radiusY, y + height - radiusY);

        int cornerWidth = radiusX * 2;
        int cornerHeight = radiusY * 2;
        DrawArc(x, y, cornerWidth, cornerHeight, 90, 90);
        DrawArc(x + width - cornerWidth, y, cornerWidth, cornerHeight, 0, 90);
        DrawArc(x, y + height - cornerHeight, cornerWidth, cornerHeight, 180, 90);
        DrawArc(x + width - cornerWidth, y + height - cornerHeight, cornerWidth, cornerHeight, 270, 90);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Square(double value) => value * value;

    // ---------------------------------------------------------------------------------
    // Lines
    // ---------------------------------------------------------------------------------

    public void DrawLine(int x1, int y1, int x2, int y2)
    {
        if (ClipIsEmpty) return;

        int ax = x1 + TranslateX, ay = y1 + TranslateY;
        int bx = x2 + TranslateX, by = y2 + TranslateY;

        if (ay == by)
        {
            FillDeviceRun(Math.Min(ax, bx), Math.Max(ax, bx) + 1, ay);
            return;
        }

        if (ax == bx)
        {
            DrawDeviceColumn(ax, Math.Min(ay, by), Math.Max(ay, by) + 1);
            return;
        }

        PlotLine(ax, ay, bx, by);
    }

    private void DrawHorizontalLine(int x1, int x2, int y)
    {
        int deviceY = y + TranslateY;
        FillDeviceRun(Math.Min(x1, x2) + TranslateX, Math.Max(x1, x2) + TranslateX + 1, deviceY);
    }

    private void DrawVerticalLine(int x, int y1, int y2)
    {
        int deviceX = x + TranslateX;
        DrawDeviceColumn(deviceX, Math.Min(y1, y2) + TranslateY, Math.Max(y1, y2) + TranslateY + 1);
    }

    /// <summary>Fills one clipped horizontal run in device coordinates.</summary>
    private void FillDeviceRun(int left, int right, int y)
    {
        if (y < _clipTop || y >= _clipBottom) return;

        left = Math.Max(left, _clipLeft);
        right = Math.Min(right, _clipRight);
        if (left >= right) return;

        if (Stroke == StrokeStyle.Dotted)
        {
            int[] dotted = _target.Pixels;
            int rowStart = y * _target.Width;
            for (int x = left; x < right; x += 2) dotted[rowStart + x] = Color;
            return;
        }

        _target.Pixels.AsSpan((y * _target.Width) + left, right - left).Fill(Color);
    }

    private void DrawDeviceColumn(int x, int top, int bottom)
    {
        if (x < _clipLeft || x >= _clipRight) return;

        top = Math.Max(top, _clipTop);
        bottom = Math.Min(bottom, _clipBottom);
        if (top >= bottom) return;

        int[] pixels = _target.Pixels;
        int stride = _target.Width;
        int color = Color;
        int step = Stroke == StrokeStyle.Dotted ? 2 : 1;

        for (int y = top; y < bottom; y += step) pixels[(y * stride) + x] = color;
    }

    /// <summary>Bresenham line with a per-pixel clip test, used only for diagonals.</summary>
    private void PlotLine(int x1, int y1, int x2, int y2)
    {
        int dx = Math.Abs(x2 - x1);
        int dy = -Math.Abs(y2 - y1);
        int stepX = x1 < x2 ? 1 : -1;
        int stepY = y1 < y2 ? 1 : -1;
        int error = dx + dy;

        int[] pixels = _target.Pixels;
        int stride = _target.Width;
        int color = Color;
        bool dotted = Stroke == StrokeStyle.Dotted;
        int counter = 0;

        while (true)
        {
            if ((!dotted || (counter++ & 1) == 0) &&
                x1 >= _clipLeft && x1 < _clipRight && y1 >= _clipTop && y1 < _clipBottom)
            {
                pixels[(y1 * stride) + x1] = color;
            }

            if (x1 == x2 && y1 == y2) break;

            int doubled = error * 2;
            if (doubled >= dy)
            {
                error += dy;
                x1 += stepX;
            }
            if (doubled <= dx)
            {
                error += dx;
                y1 += stepY;
            }
        }
    }

    // ---------------------------------------------------------------------------------
    // Triangles and arcs
    // ---------------------------------------------------------------------------------

    public void FillTriangle(int x1, int y1, int x2, int y2, int x3, int y3)
    {
        if (ClipIsEmpty) return;

        int ax = x1 + TranslateX, ay = y1 + TranslateY;
        int bx = x2 + TranslateX, by = y2 + TranslateY;
        int cx = x3 + TranslateX, cy = y3 + TranslateY;

        // Sort by y so the triangle splits into an upper and a lower flat-edged half.
        if (ay > by) { (ax, ay, bx, by) = (bx, by, ax, ay); }
        if (ay > cy) { (ax, ay, cx, cy) = (cx, cy, ax, ay); }
        if (by > cy) { (bx, by, cx, cy) = (cx, cy, bx, by); }

        int top = Math.Max(ay, _clipTop);
        int bottom = Math.Min(cy + 1, _clipBottom);

        for (int y = top; y < bottom; y++)
        {
            int left, right;

            if (y < by)
            {
                left = InterpolateX(ax, ay, bx, by, y);
                right = InterpolateX(ax, ay, cx, cy, y);
            }
            else
            {
                left = InterpolateX(bx, by, cx, cy, y);
                right = InterpolateX(ax, ay, cx, cy, y);
            }

            if (left > right) (left, right) = (right, left);
            FillDeviceRun(left, right + 1, y);
        }
    }

    private static int InterpolateX(int x1, int y1, int x2, int y2, int y)
    {
        if (y2 == y1) return x1;
        return x1 + (int)(((long)(x2 - x1) * (y - y1)) / (y2 - y1));
    }

    public void DrawArc(int x, int y, int width, int height, int startAngle, int arcAngle)
    {
        if (width <= 0 || height <= 0 || arcAngle == 0) return;

        double centreX = x + TranslateX + (width / 2.0);
        double centreY = y + TranslateY + (height / 2.0);
        double radiusX = width / 2.0;
        double radiusY = height / 2.0;

        // One step per pixel of the longer axis keeps the outline gapless without oversampling.
        int steps = Math.Max(8, (int)(Math.Abs(arcAngle) * Math.Max(radiusX, radiusY) / 30.0) + 8);
        double stepAngle = arcAngle * Math.PI / 180.0 / steps;
        double start = startAngle * Math.PI / 180.0;

        int previousX = int.MinValue, previousY = 0;
        for (int i = 0; i <= steps; i++)
        {
            double angle = start + (stepAngle * i);
            // MIDP angles run counter-clockwise while screen y grows downwards.
            int px = (int)Math.Round(centreX + (radiusX * Math.Cos(angle)));
            int py = (int)Math.Round(centreY - (radiusY * Math.Sin(angle)));

            if (previousX != int.MinValue) PlotLine(previousX, previousY, px, py);
            previousX = px;
            previousY = py;
        }
    }

    public void FillArc(int x, int y, int width, int height, int startAngle, int arcAngle)
    {
        if (width <= 0 || height <= 0 || arcAngle == 0 || ClipIsEmpty) return;

        if (Math.Abs(arcAngle) >= 360)
        {
            FillEllipse(x, y, width, height);
            return;
        }

        double centreX = x + TranslateX + (width / 2.0);
        double centreY = y + TranslateY + (height / 2.0);
        double radiusX = width / 2.0;
        double radiusY = height / 2.0;

        double from = arcAngle > 0 ? startAngle : startAngle + arcAngle;
        double sweep = Math.Abs(arcAngle);

        int top = Math.Max(_clipTop, (int)Math.Floor(centreY - radiusY));
        int bottom = Math.Min(_clipBottom, (int)Math.Ceiling(centreY + radiusY) + 1);
        int left = Math.Max(_clipLeft, (int)Math.Floor(centreX - radiusX));
        int right = Math.Min(_clipRight, (int)Math.Ceiling(centreX + radiusX) + 1);

        int[] pixels = _target.Pixels;
        int stride = _target.Width;
        int color = Color;

        for (int py = top; py < bottom; py++)
        {
            double normalisedY = (py + 0.5 - centreY) / radiusY;
            int rowStart = py * stride;

            for (int px = left; px < right; px++)
            {
                double normalisedX = (px + 0.5 - centreX) / radiusX;
                if ((normalisedX * normalisedX) + (normalisedY * normalisedY) > 1.0) continue;

                double angle = Math.Atan2(-normalisedY, normalisedX) * 180.0 / Math.PI;
                if (angle < 0) angle += 360;

                double delta = angle - from;
                while (delta < 0) delta += 360;
                if (delta > sweep) continue;

                pixels[rowStart + px] = color;
            }
        }
    }

    private void FillEllipse(int x, int y, int width, int height)
    {
        double centreX = x + TranslateX + (width / 2.0);
        double centreY = y + TranslateY + (height / 2.0);
        double radiusX = width / 2.0;
        double radiusY = height / 2.0;

        int top = Math.Max(_clipTop, (int)Math.Floor(centreY - radiusY));
        int bottom = Math.Min(_clipBottom, (int)Math.Ceiling(centreY + radiusY) + 1);

        for (int py = top; py < bottom; py++)
        {
            double normalisedY = (py + 0.5 - centreY) / radiusY;
            double squared = 1.0 - (normalisedY * normalisedY);
            if (squared <= 0) continue;

            double half = radiusX * Math.Sqrt(squared);
            FillDeviceRun((int)Math.Round(centreX - half), (int)Math.Round(centreX + half), py);
        }
    }

    // ---------------------------------------------------------------------------------
    // Pixel transfer
    // ---------------------------------------------------------------------------------

    /// <summary>Implements <c>Graphics.drawRGB</c>, the raw pixel upload path.</summary>
    public void DrawRgb(
        ReadOnlySpan<int> source,
        int offset,
        int scanLength,
        int x,
        int y,
        int width,
        int height,
        bool processAlpha)
    {
        if (width <= 0 || height <= 0 || ClipIsEmpty) return;

        int destinationLeft = x + TranslateX;
        int destinationTop = y + TranslateY;

        int startColumn = Math.Max(0, _clipLeft - destinationLeft);
        int startRow = Math.Max(0, _clipTop - destinationTop);
        int endColumn = Math.Min(width, _clipRight - destinationLeft);
        int endRow = Math.Min(height, _clipBottom - destinationTop);
        if (startColumn >= endColumn || startRow >= endRow) return;

        int[] pixels = _target.Pixels;
        int stride = _target.Width;

        for (int row = startRow; row < endRow; row++)
        {
            int sourceIndex = offset + (row * scanLength) + startColumn;
            int destinationIndex = ((destinationTop + row) * stride) + destinationLeft + startColumn;

            if (!processAlpha)
            {
                for (int column = startColumn; column < endColumn; column++)
                    pixels[destinationIndex++] = PixelBlend.Opaque(source[sourceIndex++]);
            }
            else
            {
                for (int column = startColumn; column < endColumn; column++)
                    pixels[destinationIndex] = PixelBlend.Over(source[sourceIndex++], pixels[destinationIndex++]);
            }
        }
    }

    /// <summary>Implements <c>Graphics.copyArea</c> within the current target.</summary>
    public void CopyArea(int sourceX, int sourceY, int width, int height, int destinationX, int destinationY, int anchor)
    {
        if (width <= 0 || height <= 0) return;

        int left = Anchor.ResolveX(destinationX, width, anchor);
        int top = Anchor.ResolveY(destinationY, height, anchor);

        // The regions may overlap, so the source is captured before anything is written.
        var scratch = new int[width * height];
        int stride = _target.Width;
        int[] pixels = _target.Pixels;

        for (int row = 0; row < height; row++)
        {
            int y = sourceY + TranslateY + row;
            if (y < 0 || y >= _target.Height) continue;

            for (int column = 0; column < width; column++)
            {
                int x = sourceX + TranslateX + column;
                if (x < 0 || x >= stride) continue;
                scratch[(row * width) + column] = pixels[(y * stride) + x];
            }
        }

        DrawRgb(scratch, 0, width, left, top, width, height, processAlpha: false);
    }

    // ---------------------------------------------------------------------------------
    // Images
    // ---------------------------------------------------------------------------------

    public void DrawImage(Surface image, int x, int y, int anchor)
    {
        int left = Anchor.ResolveX(x, image.Width, anchor);
        int top = Anchor.ResolveY(y, image.Height, anchor);
        Blit(image, 0, 0, image.Width, image.Height, left, top, SpriteTransform.None);
    }

    /// <summary>Implements <c>drawRegion</c>: a sub-rectangle drawn under a rigid transform.</summary>
    public void DrawRegion(
        Surface image,
        int sourceX,
        int sourceY,
        int sourceWidth,
        int sourceHeight,
        int transform,
        int destinationX,
        int destinationY,
        int anchor)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0) return;

        int drawnWidth = SpriteTransform.SwapsAxes(transform) ? sourceHeight : sourceWidth;
        int drawnHeight = SpriteTransform.SwapsAxes(transform) ? sourceWidth : sourceHeight;

        int left = Anchor.ResolveX(destinationX, drawnWidth, anchor);
        int top = Anchor.ResolveY(destinationY, drawnHeight, anchor);

        Blit(image, sourceX, sourceY, sourceWidth, sourceHeight, left, top, transform);
    }

    /// <summary>
    /// The single blit routine every image draw funnels into.
    /// </summary>
    /// <remarks>
    /// The untransformed opaque case is separated out and copies whole rows, because that is
    /// what background and tile layers do thousands of times per second. Transformed and
    /// translucent draws walk pixel by pixel, which is unavoidable but far rarer.
    /// </remarks>
    public void Blit(
        Surface image,
        int sourceX,
        int sourceY,
        int sourceWidth,
        int sourceHeight,
        int destinationX,
        int destinationY,
        int transform)
    {
        if (ClipIsEmpty || sourceWidth <= 0 || sourceHeight <= 0) return;

        // Clamp the source rectangle to the image before anything else.
        if (sourceX < 0) { sourceWidth += sourceX; destinationX -= sourceX; sourceX = 0; }
        if (sourceY < 0) { sourceHeight += sourceY; destinationY -= sourceY; sourceY = 0; }
        if (sourceX + sourceWidth > image.Width) sourceWidth = image.Width - sourceX;
        if (sourceY + sourceHeight > image.Height) sourceHeight = image.Height - sourceY;
        if (sourceWidth <= 0 || sourceHeight <= 0) return;

        int drawnWidth = SpriteTransform.SwapsAxes(transform) ? sourceHeight : sourceWidth;
        int drawnHeight = SpriteTransform.SwapsAxes(transform) ? sourceWidth : sourceHeight;

        int left = destinationX + TranslateX;
        int top = destinationY + TranslateY;

        int startColumn = Math.Max(0, _clipLeft - left);
        int startRow = Math.Max(0, _clipTop - top);
        int endColumn = Math.Min(drawnWidth, _clipRight - left);
        int endRow = Math.Min(drawnHeight, _clipBottom - top);
        if (startColumn >= endColumn || startRow >= endRow) return;

        int[] destination = _target.Pixels;
        int[] source = image.Pixels;
        int destinationStride = _target.Width;
        int sourceStride = image.Width;

        if (transform == SpriteTransform.None && !image.HasAlpha)
        {
            for (int row = startRow; row < endRow; row++)
            {
                int sourceIndex = ((sourceY + row) * sourceStride) + sourceX + startColumn;
                int destinationIndex = ((top + row) * destinationStride) + left + startColumn;
                source.AsSpan(sourceIndex, endColumn - startColumn)
                    .CopyTo(destination.AsSpan(destinationIndex));
            }
            return;
        }

        for (int row = startRow; row < endRow; row++)
        {
            int destinationIndex = ((top + row) * destinationStride) + left + startColumn;

            for (int column = startColumn; column < endColumn; column++)
            {
                MapSource(transform, column, row, sourceWidth, sourceHeight, out int sx, out int sy);
                int pixel = source[((sourceY + sy) * sourceStride) + sourceX + sx];
                destination[destinationIndex] = PixelBlend.Over(pixel, destination[destinationIndex]);
                destinationIndex++;
            }
        }
    }

    /// <summary>Maps a destination offset back to the source pixel it came from.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void MapSource(
        int transform,
        int column,
        int row,
        int sourceWidth,
        int sourceHeight,
        out int sourceColumn,
        out int sourceRow)
    {
        switch (transform)
        {
            case SpriteTransform.None:
                sourceColumn = column;
                sourceRow = row;
                break;

            case SpriteTransform.Mirror:
                sourceColumn = sourceWidth - 1 - column;
                sourceRow = row;
                break;

            case SpriteTransform.Rotate180:
                sourceColumn = sourceWidth - 1 - column;
                sourceRow = sourceHeight - 1 - row;
                break;

            case SpriteTransform.MirrorRotate180:
                sourceColumn = column;
                sourceRow = sourceHeight - 1 - row;
                break;

            case SpriteTransform.Rotate90:
                sourceColumn = row;
                sourceRow = sourceHeight - 1 - column;
                break;

            case SpriteTransform.Rotate270:
                sourceColumn = sourceWidth - 1 - row;
                sourceRow = column;
                break;

            case SpriteTransform.MirrorRotate90:
                sourceColumn = row;
                sourceRow = column;
                break;

            case SpriteTransform.MirrorRotate270:
                sourceColumn = sourceWidth - 1 - row;
                sourceRow = sourceHeight - 1 - column;
                break;

            default:
                sourceColumn = column;
                sourceRow = row;
                break;
        }
    }

    // ---------------------------------------------------------------------------------
    // Text
    // ---------------------------------------------------------------------------------

    public void DrawString(ReadOnlySpan<char> text, int x, int y, int anchor)
    {
        if (Font is not { } font || text.IsEmpty || ClipIsEmpty) return;

        int width = font.StringWidth(text);
        int left = Anchor.ResolveX(x, width, anchor);
        int top = Anchor.ResolveY(y, font.Height, anchor, font.Ascent);

        int pen = left;
        foreach (char character in text)
        {
            Glyph glyph = font.GetGlyph(character);
            if (glyph.Width > 0 && glyph.Height > 0)
                DrawGlyph(font.Atlas, glyph, pen + glyph.OffsetX, top + glyph.OffsetY);
            pen += glyph.Advance;
        }
    }

    public void DrawChar(char character, int x, int y, int anchor)
    {
        Span<char> single = [character];
        DrawString(single, x, y, anchor);
    }

    /// <summary>
    /// Stamps one glyph, taking its shape from the atlas alpha and its colour from the context.
    /// </summary>
    private void DrawGlyph(Surface atlas, Glyph glyph, int destinationX, int destinationY)
    {
        int left = destinationX + TranslateX;
        int top = destinationY + TranslateY;

        int startColumn = Math.Max(0, _clipLeft - left);
        int startRow = Math.Max(0, _clipTop - top);
        int endColumn = Math.Min((int)glyph.Width, _clipRight - left);
        int endRow = Math.Min((int)glyph.Height, _clipBottom - top);
        if (startColumn >= endColumn || startRow >= endRow) return;

        int[] destination = _target.Pixels;
        int[] coverage = atlas.Pixels;
        int destinationStride = _target.Width;
        int atlasStride = atlas.Width;
        int rgb = Color & 0x00FFFFFF;

        for (int row = startRow; row < endRow; row++)
        {
            int atlasIndex = ((glyph.Y + row) * atlasStride) + glyph.X + startColumn;
            int destinationIndex = ((top + row) * destinationStride) + left + startColumn;

            for (int column = startColumn; column < endColumn; column++)
            {
                int alpha = (int)((uint)coverage[atlasIndex++] >> 24);
                if (alpha != 0)
                {
                    destination[destinationIndex] =
                        PixelBlend.Over((alpha << 24) | rgb, destination[destinationIndex]);
                }
                destinationIndex++;
            }
        }
    }
}
