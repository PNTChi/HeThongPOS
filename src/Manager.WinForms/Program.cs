using Manager.Core;
using Manager.Infrastructure;
using Cashier.MongoIntegration;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Text.Json;

namespace Manager.WinForms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Local storage directories
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var managerDir = Path.Combine(localAppData, "Nhom11", "ManagerData");
        var cashierDir = Path.Combine(localAppData, "Nhom11", "CashierDemo");
        var receiptsDir = Path.Combine(cashierDir, "receipts");

        Directory.CreateDirectory(managerDir);
        Directory.CreateDirectory(receiptsDir);

        // Resolve DemoCatalog.json location
        var catalogCandidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Data", "DemoCatalog.json"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Cashier.WinForms", "Data", "DemoCatalog.json"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Windows_Demo", "Data", "DemoCatalog.json"),
            Path.Combine(managerDir, "DemoCatalog.json")
        };

        var catalogPath = catalogCandidates.FirstOrDefault(File.Exists) ?? catalogCandidates[0];

        // Ensure catalog exists
        if (!File.Exists(catalogPath))
        {
            var fallback = catalogCandidates.FirstOrDefault(File.Exists);
            if (fallback != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(catalogPath)!);
                File.Copy(fallback, catalogPath, true);
            }
        }

        var mongoConnectionString = MongoConnectionStringProvider.Resolve();

        var mongoClient = new MongoClient(mongoConnectionString);

        var database = mongoClient.GetDatabase("CashierDb");

        var catalogStore = new MongoCatalogStore(database);
        SeedCatalogToEightyProductsOnce(database);
        NormalizeSeedProductContentOnce(database);
        NormalizeCatalogProductIdsOnce(database);
        var accountStore = new MongoAccountStore(database);
        var shiftStore = new MongoShiftStore(database);
        var orderMapper = new CashierOrderMapper();
        var orderRepository = new MongoOrderRepository(
            database.GetCollection<BsonDocument>("orders"), orderMapper);
        var receiptHistory = new MongoReceiptHistory(
            database.GetCollection<BsonDocument>("orders"), orderMapper);
        var mongoTransactionReader = new MongoTransactionReader(database, orderMapper);
        var legacyTransactionReader = new JsonTransactionReader(receiptsDir);

        // Copy prior JSON receipts into MongoDB once; stable order IDs make this safe on later launches.
        var existingMongoOrderIds = mongoTransactionReader.ReadAllReceiptsAsync()
            .GetAwaiter().GetResult()
            .Select(receipt => receipt.OrderId)
            .ToHashSet();
        var migratedReceiptCount = 0;
        foreach (var receipt in legacyTransactionReader.ReadAllReceiptsAsync().GetAwaiter().GetResult())
        {
            if (existingMongoOrderIds.Contains(receipt.OrderId))
            {
                continue;
            }

            orderRepository.SaveAsync(receipt).GetAwaiter().GetResult();
            existingMongoOrderIds.Add(receipt.OrderId);
            migratedReceiptCount++;
        }

        if (migratedReceiptCount > 0)
        {
            MessageBox.Show(
                $"Đã chuyển {migratedReceiptCount} hóa đơn cũ từ JSON sang MongoDB.",
                "Đồng bộ dữ liệu lịch sử",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        var transactionReader = new CombinedTransactionReader(
            mongoTransactionReader,
            legacyTransactionReader);

        var controller = new ManagerController(catalogStore, accountStore, shiftStore, transactionReader);

        while (true)
        {
            using var loginForm = new LoginForm(controller);
            if (loginForm.ShowDialog() != DialogResult.OK)
            {
                break;
            }

            var mainForm = new MainManagerForm(
                controller, catalogPath, receiptsDir, cashierDir, orderRepository, receiptHistory);
            Application.Run(mainForm);

            // If main form closed due to logout, loop back to login form; otherwise exit
            if (controller.CurrentUser == null)
            {
                continue;
            }
            break;
        }
    }

    private static void SeedCatalogToEightyProductsOnce(IMongoDatabase database)
    {
        const string seedMarkerId = "catalog-80-products-v3";
        var metadata = database.GetCollection<BsonDocument>("app_metadata");
        if (metadata.Find(Builders<BsonDocument>.Filter.Eq("_id", seedMarkerId)).Any())
        {
            return;
        }

        var filePath = Path.Combine(AppContext.BaseDirectory, "Data", "AdditionalProducts.json");
        if (!File.Exists(filePath))
        {
            return;
        }

        var catalog = database.GetCollection<BsonDocument>("catalog");
        var productCount = catalog.CountDocuments(FilterDefinition<BsonDocument>.Empty);
        const long targetProductCount = 80;
        using var json = JsonDocument.Parse(File.ReadAllText(filePath));
        foreach (var element in json.RootElement.EnumerateArray())
        {
            var product = BsonDocument.Parse(element.GetRawText());
            var id = product["Id"].AsString;
            var productFilters = new List<FilterDefinition<BsonDocument>>
            {
                Builders<BsonDocument>.Filter.Eq("_id", id),
                Builders<BsonDocument>.Filter.Eq("Id", id)
            };
            if (id.StartsWith("MON-", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(id.AsSpan(4), out var legacyNumber))
            {
                var previousThreeDigitId = $"MON-{legacyNumber:D3}";
                if (!string.Equals(previousThreeDigitId, id, StringComparison.OrdinalIgnoreCase))
                {
                    productFilters.Add(Builders<BsonDocument>.Filter.Eq("_id", previousThreeDigitId));
                    productFilters.Add(Builders<BsonDocument>.Filter.Eq("Id", previousThreeDigitId));
                }
            }

            var filter = Builders<BsonDocument>.Filter.Or(productFilters);
            var existing = catalog.Find(filter).FirstOrDefault();
            if (existing is not null)
            {
                // Earlier seed data had these same five products without modifiers.
                // Add the options once, but preserve any choices a manager already configured.
                var hasOptions = existing.TryGetValue("OptionGroups", out var optionGroups) &&
                    optionGroups.IsBsonArray && optionGroups.AsBsonArray.Count > 0;
                if (!hasOptions && product["OptionGroups"].AsBsonArray.Count > 0)
                {
                    catalog.UpdateOne(filter,
                        Builders<BsonDocument>.Update.Set("OptionGroups", product["OptionGroups"]));
                }

                continue;
            }

            if (productCount >= targetProductCount)
            {
                continue;
            }

            catalog.InsertOne(product);
            productCount++;
        }

        metadata.InsertOne(new BsonDocument
        {
            { "_id", seedMarkerId },
            { "AppliedAt", DateTime.UtcNow }
        });
    }

    private static void NormalizeSeedProductContentOnce(IMongoDatabase database)
    {
        const string markerId = "catalog-product-content-v1";
        var metadata = database.GetCollection<BsonDocument>("app_metadata");
        if (metadata.Find(Builders<BsonDocument>.Filter.Eq("_id", markerId)).Any()) return;

        var filePath = Path.Combine(AppContext.BaseDirectory, "Data", "AdditionalProducts.json");
        if (!File.Exists(filePath)) return;

        var catalog = database.GetCollection<BsonDocument>("catalog");
        using var json = JsonDocument.Parse(File.ReadAllText(filePath));
        foreach (var element in json.RootElement.EnumerateArray())
        {
            var id = element.GetProperty("Id").GetString();
            if (string.IsNullOrWhiteSpace(id)) continue;
            var filter = Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Eq("Id", id),
                Builders<BsonDocument>.Filter.Eq("_id", id));
            var optionGroups = MongoDB.Bson.Serialization.BsonSerializer.Deserialize<BsonArray>(
                element.GetProperty("OptionGroups").GetRawText());
            catalog.UpdateMany(filter, Builders<BsonDocument>.Update
                .Set("Name", element.GetProperty("Name").GetString())
                .Set("Category", element.GetProperty("Category").GetString())
                .Set("OptionGroups", optionGroups)
                .Set("ImagePath", element.GetProperty("ImagePath").GetString()));
        }

        var demoCatalogPath = Path.Combine(AppContext.BaseDirectory, "Data", "DemoCatalog.json");
        if (File.Exists(demoCatalogPath))
        {
            using var demoJson = JsonDocument.Parse(File.ReadAllText(demoCatalogPath));
            foreach (var element in demoJson.RootElement.GetProperty("Menu").EnumerateArray())
            {
                var id = element.GetProperty("Id").GetString();
                if (string.IsNullOrWhiteSpace(id)) continue;
                var filter = Builders<BsonDocument>.Filter.Or(
                    Builders<BsonDocument>.Filter.Eq("Id", id),
                    Builders<BsonDocument>.Filter.Eq("_id", id));
                catalog.UpdateMany(filter,
                    Builders<BsonDocument>.Update
                        .Set("Name", element.GetProperty("Name").GetString())
                        .Set("Category", element.GetProperty("Category").GetString())
                        .Set("ImagePath", $"Images/{id.ToLowerInvariant()}.png"));
            }
        }

        metadata.InsertOne(new BsonDocument
        {
            { "_id", markerId },
            { "AppliedAt", DateTime.UtcNow },
            { "UpdatedProducts", 70 }
        });
    }

    private static void NormalizeCatalogProductIdsOnce(IMongoDatabase database)
    {
        const string migrationMarkerId = "catalog-product-ids-mon-v1";
        var metadata = database.GetCollection<BsonDocument>("app_metadata");
        if (metadata.Find(Builders<BsonDocument>.Filter.Eq("_id", migrationMarkerId)).Any())
        {
            return;
        }

        // Keep the original ten demo products in DemoCatalog.json order, followed
        // by the numbered seed products, so their new codes remain predictable.
        var originalProductOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["CA-PHE-DEN"] = 0,
            ["CA-PHE-SUA"] = 1,
            ["MACCHIATO"] = 2,
            ["TRA-SUA"] = 3,
            ["TRA-DAO"] = 4,
            ["TRA-CHANH"] = 5,
            ["BURGER"] = 6,
            ["KHOAI"] = 7,
            ["GA"] = 8,
            ["NUOC"] = 9
        };

        var catalog = database.GetCollection<BsonDocument>("catalog");
        var products = catalog.Find(FilterDefinition<BsonDocument>.Empty).ToList();
        var orderedProducts = products
            .OrderBy(document =>
            {
                var id = document.GetValue("Id", document.GetValue("_id", "")).ToString() ?? string.Empty;
                return originalProductOrder.ContainsKey(id) ? 0 :
                    id.StartsWith("MON-", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(id.AsSpan(4), out _) ? 1 : 2;
            })
            .ThenBy(document =>
            {
                var id = document.GetValue("Id", document.GetValue("_id", "")).ToString() ?? string.Empty;
                return originalProductOrder.TryGetValue(id, out var order) ? order : int.MaxValue;
            })
            .ThenBy(document =>
            {
                var id = document.GetValue("Id", document.GetValue("_id", "")).ToString() ?? string.Empty;
                return id.StartsWith("MON-", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(id.AsSpan(4), out var number) ? number : int.MaxValue;
            })
            .ThenBy(document => document.GetValue("Name", "").ToString(), StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var index = 0; index < orderedProducts.Count; index++)
        {
            var document = orderedProducts[index];
            var productCode = $"MON-{index + 1:D2}";
            var oldCode = document.GetValue("Id", document.GetValue("_id", "")).ToString() ?? string.Empty;
            var update = Builders<BsonDocument>.Update.Set("Id", productCode);
            if (!string.Equals(oldCode, productCode, StringComparison.OrdinalIgnoreCase) &&
                !document.Contains("LegacyId"))
            {
                update = update.Set("LegacyId", oldCode);
            }

            catalog.UpdateOne(
                Builders<BsonDocument>.Filter.Eq("_id", document["_id"]),
                update);
        }

        metadata.InsertOne(new BsonDocument
        {
            { "_id", migrationMarkerId },
            { "AppliedAt", DateTime.UtcNow },
            { "UpdatedProducts", orderedProducts.Count }
        });
    }
}

