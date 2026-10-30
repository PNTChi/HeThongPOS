using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Security;
using Cashier.Core;

namespace Cashier.WinForms;

// Keep the whole card a native Button: Tab, Space, Enter and screen readers can
// select products without relying on a clickable PictureBox or mouse-only panel.
internal sealed class ProductCard : Button
{
    private Image? _ownedImage;
    private readonly Font _ownedFont = new("Segoe UI", 10, FontStyle.Bold);

    public ProductCard(MenuItem item, Action onSelected, string? imageDataDirectory = null)
    {
        var status = !item.Available ? "Tạm hết món" :
            item.OptionGroups.Length > 0 ? "Có tùy chọn" : "Còn món";
        Text = item.Name + "\n" + CartRules.Money(item.BasePrice) + "\n" + status;
        AccessibleName = item.Name + ", " + CartRules.Money(item.BasePrice) + ", " + status;
        AccessibleDescription = "Chọn để thêm món vào giỏ hàng.";
        AccessibleRole = AccessibleRole.PushButton;
        UseMnemonic = false;
        AutoSize = false;
        Size = new Size(183, 210);
        MinimumSize = new Size(165, 200);
        Margin = new Padding(6);
        Padding = new Padding(10);
        FlatStyle = FlatStyle.Flat;
        BackColor = item.Available ? Ui.CardBackground : Ui.Soft;
        ForeColor = Ui.Ink;
        Font = _ownedFont;
        Cursor = Cursors.Hand;
        TextAlign = ContentAlignment.MiddleLeft;
        ImageAlign = ContentAlignment.TopCenter;
        TextImageRelation = TextImageRelation.ImageAboveText;
        UseVisualStyleBackColor = false;
        FlatAppearance.BorderColor = Ui.Border;
        FlatAppearance.MouseOverBackColor = Ui.Soft;
        FlatAppearance.MouseDownBackColor = Ui.Selection;
        _ownedImage = ProductImageLoader.TryLoadData(item.ImageData) ??
            ProductImageLoader.TryLoad(item.ImagePath,
                imageDataDirectory ?? Path.Combine(AppContext.BaseDirectory, "Data")) ?? CreatePlaceholder(item.Name);
        Image = _ownedImage;
        Click += (_, _) => onSelected();
    }

    private static Bitmap CreatePlaceholder(string name)
    {
        var bitmap = new Bitmap(ProductImageLoader.ThumbnailWidth, ProductImageLoader.ThumbnailHeight);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Ui.Soft);
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        var initials = string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(2).Select(word => word[..1])).ToUpperInvariant();
        using var initialsFont = new Font("Segoe UI", 25, FontStyle.Bold);
        using var captionFont = new Font("Segoe UI", 8);
        using var initialsBrush = new SolidBrush(Ui.Brand);
        using var captionBrush = new SolidBrush(Ui.Muted);
        using var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString(initials, initialsFont, initialsBrush, new RectangleF(0, 7, bitmap.Width, 47), centered);
        graphics.DrawString("Chưa có ảnh", captionFont, captionBrush, new RectangleF(0, 57, bitmap.Width, 22), centered);
        return bitmap;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Image = null;
            _ownedImage?.Dispose();
            _ownedImage = null;
            _ownedFont.Dispose();
        }
        base.Dispose(disposing);
    }
}

// Images are presentation metadata only; changing or uploading them belongs to
// Manager. Failed images must never prevent Cashier from choosing a valid item.
internal static class ProductImageLoader
{
    internal const int ThumbnailWidth = 152;
    internal const int ThumbnailHeight = 88;
    private const long MaximumFileBytes = 8 * 1024 * 1024;
    private const long MaximumPixels = 25_000_000;

    internal static Image? TryLoad(string? imagePath, string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) return null;
        try
        {
            // Only portable, local paths under Data/Images are accepted. Reject
            // URLs, absolute paths, alternate data streams and traversal early.
            var relative = imagePath.Trim().Replace('\\', '/');
            if (Path.IsPathRooted(relative) || relative.Contains(':')) return null;
            var segments = relative.Split('/');
            if (segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or "..")) return null;
            if (segments[0].Equals("Data", StringComparison.OrdinalIgnoreCase)) segments = segments[1..];
            if (segments.Length < 2 || !segments[0].Equals("Images", StringComparison.OrdinalIgnoreCase)) return null;
            var extension = Path.GetExtension(segments[^1]).ToLowerInvariant();
            if (extension is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif")) return null;

            var root = Path.GetFullPath(dataDirectory);
            var imagesRoot = Path.GetFullPath(Path.Combine(root, "Images")) + Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(Path.Combine(root, Path.Combine(segments)));
            if (!fullPath.StartsWith(imagesRoot, StringComparison.OrdinalIgnoreCase)) return null;
            // Do not follow junctions or symbolic links outside the image tree.
            var current = root;
            if (IsReparsePoint(current)) return null;
            foreach (var segment in segments)
            {
                current = Path.Combine(current, segment);
                if (IsReparsePoint(current)) return null;
            }

            var file = new FileInfo(fullPath);
            if (file.Length == 0 || file.Length > MaximumFileBytes) return null;
            // Read to memory and draw to a detached bitmap. The original file is
            // never locked while the card is shown, so Manager can replace it.
            using var stream = new MemoryStream(File.ReadAllBytes(fullPath));
            using var source = Image.FromStream(stream, false, true);
            if (source.Width <= 0 || source.Height <= 0 ||
                (long)source.Width * source.Height > MaximumPixels) return null;
            var thumbnail = new Bitmap(ThumbnailWidth, ThumbnailHeight);
            try
            {
                using var graphics = Graphics.FromImage(thumbnail);
                graphics.Clear(Ui.Soft);
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                var ratio = Math.Min((float)ThumbnailWidth / source.Width, (float)ThumbnailHeight / source.Height);
                var width = Math.Max(1, (int)(source.Width * ratio));
                var height = Math.Max(1, (int)(source.Height * ratio));
                graphics.DrawImage(source, (ThumbnailWidth - width) / 2, (ThumbnailHeight - height) / 2, width, height);
                return thumbnail;
            }
            catch
            {
                thumbnail.Dispose();
                throw;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException or ExternalException or OutOfMemoryException or SecurityException)
        {
            return null;
        }
    }

    internal static Image? TryLoadData(string? base64Image)
    {
        if (string.IsNullOrWhiteSpace(base64Image) || base64Image.Length > MaximumFileBytes * 4 / 3 + 8) return null;
        try
        {
            var bytes = Convert.FromBase64String(base64Image);
            if (bytes.Length == 0 || bytes.Length > MaximumFileBytes) return null;
            using var stream = new MemoryStream(bytes, writable: false);
            using var source = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
            if (source.Width <= 0 || source.Height <= 0 || (long)source.Width * source.Height > MaximumPixels) return null;

            var thumbnail = new Bitmap(ThumbnailWidth, ThumbnailHeight);
            try
            {
                using var graphics = Graphics.FromImage(thumbnail);
                graphics.Clear(Ui.Soft);
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                var ratio = Math.Min((float)ThumbnailWidth / source.Width, (float)ThumbnailHeight / source.Height);
                var width = Math.Max(1, (int)(source.Width * ratio));
                var height = Math.Max(1, (int)(source.Height * ratio));
                graphics.DrawImage(source, (ThumbnailWidth - width) / 2, (ThumbnailHeight - height) / 2, width, height);
                return thumbnail;
            }
            catch
            {
                thumbnail.Dispose();
                throw;
            }
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or
            ExternalException or OutOfMemoryException or SecurityException)
        {
            return null;
        }
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}
