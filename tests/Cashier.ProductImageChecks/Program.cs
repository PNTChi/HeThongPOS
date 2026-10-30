using System.Collections.Immutable;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cashier.Core;
using Cashier.Infrastructure;
using Cashier.WinForms;
using Manager.Infrastructure;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        // All fixtures live in this test's output folder. No catalog is edited.
        var fixtureRoot = Path.Combine(AppContext.BaseDirectory, "image-fixtures");
        var images = Path.Combine(fixtureRoot, "Images");
        Directory.CreateDirectory(images);
        var validPath = Path.Combine(images, "valid.png");
        using (var bitmap = new Bitmap(32, 16))
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Coral);
            bitmap.Save(validPath, ImageFormat.Png);
        }
        File.WriteAllText(Path.Combine(images, "corrupt.png"), "This is not an image.");
        var passed = 0;
        void Check(bool success, string message)
        {
            if (!success) throw new InvalidOperationException(message);
            Console.WriteLine("PASS " + message);
            passed++;
        }

        try
        {
            using (var loaded = ProductImageLoader.TryLoad("Images/valid.png", fixtureRoot))
            {
                Check(loaded is { Width: 152, Height: 88 }, "Valid image becomes a detached thumbnail");
                var moved = Path.Combine(images, "renamed.png");
                File.Move(validPath, moved, true);
                File.Move(moved, validPath, true);
                Check(loaded is not null && loaded.GetPixelAtCenter().ToArgb() == Color.Coral.ToArgb(),
                    "Manager can replace the original while Cashier displays the image");
            }
            using (var loaded = ProductImageLoader.TryLoad("Data/Images/valid.png", fixtureRoot))
                Check(loaded is not null, "Data/Images portable catalog path is accepted");
            foreach (var invalid in new string?[]
            {
                null, "", "Images/missing.png", "Images/corrupt.png", "Images/file.svg",
                "../Images/valid.png", "Images/../Images/valid.png", validPath,
                "https://example.com/product.png", "Images/valid.png:stream", "ImagesOther/valid.png"
            })
            {
                using var loaded = ProductImageLoader.TryLoad(invalid, fixtureRoot);
                Check(loaded is null, "Fallback safely: " + (invalid ?? "<null>"));
            }
            var item = new MenuItem("TEST", "Cà phê sữa", "Cà phê", 25000, true,
                ImmutableArray<OptionGroup>.Empty);
            var oldCatalogJson = JsonSerializer.SerializeToNode(item)!.AsObject();
            oldCatalogJson.Remove("ImagePath");
            var restored = oldCatalogJson.Deserialize<MenuItem>();
            Check(restored is { ImagePath: null } && restored.Name == item.Name,
                "Existing MenuItem constructor and catalogs remain compatible");
            var selected = 0;
            using (var card = new ProductCard(item, () => selected++, fixtureRoot))
            {
                Check(card.Image is not null && card.AccessibleRole == AccessibleRole.PushButton && card.TabStop,
                    "No-image card stays a keyboard accessible button with a placeholder");
                card.PerformClick();
                Check(selected == 1, "Selecting the product calls the existing add-item action once");
            }
            using (var card = new ProductCard(item with { ImagePath = "Images/corrupt.png" }, () => { }, fixtureRoot))
                Check(card.Image is not null, "Corrupt image does not block product selection");
            var catalogPath = Path.Combine(fixtureRoot, "manager-catalog.json");
            // Native Button construction installs a UI synchronization context;
            // run the async storage fixture away from that non-pumping thread.
            var cashierCatalog = Task.Run(async () =>
            {
                var managerCatalog = new JsonCatalogStore(catalogPath);
                await managerCatalog.SaveMenuAsync([item with { ImagePath = "Images/valid.png" }]);
                var managedMenu = await managerCatalog.GetMenuAsync();
                await managerCatalog.SaveMenuAsync([managedMenu[0] with { BasePrice = 27000 }]);
                await managerCatalog.SavePromotionsAsync([]);
                return await new JsonSalesDataSource(catalogPath, new DemoSessionProvider()).ReadAsync();
            }).GetAwaiter().GetResult();
            Check(cashierCatalog.Menu[0] is { ImagePath: "Images/valid.png", BasePrice: 27000 },
                "Manager save/edit and promotion save preserve optional image metadata for Cashier");
            using (var managedImage = ProductImageLoader.TryLoad(cashierCatalog.Menu[0].ImagePath, fixtureRoot))
                Check(managedImage is not null, "Cashier displays image metadata after Manager JSON roundtrip");
            Console.WriteLine($"{passed} image/card checks passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static Color GetPixelAtCenter(this Image image) => ((Bitmap)image).GetPixel(image.Width / 2, image.Height / 2);
}
