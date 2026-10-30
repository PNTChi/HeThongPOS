using Cashier.Core;
using Cashier.Infrastructure;
using Cashier.MongoIntegration;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Collections.Immutable;

namespace Cashier.WinForms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        // One running workstation per profile avoids two local checkout journals overwriting each other.
        using var singleInstance = new Mutex(true, "Nhom11.Cashier.Demo." + Environment.UserName, out var first);
        if (!first)
        {
            MessageBox.Show("Cashier đang mở trong phiên Windows này.", "POS Nhóm 11");
            return;
        }
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Nhom11", "CashierDemo");

        // MongoDB connection
        var mongoConnectionString = MongoConnectionStringProvider.Resolve();

        var client = new MongoClient(mongoConnectionString);
        var database = client.GetDatabase("CashierDb");

        // Load catalog and promotions from MongoDB
        var catalogCollection = database.GetCollection<BsonDocument>("catalog");
        var promotionsCollection = database.GetCollection<BsonDocument>("promotions");

        // Map BSON documents to domain models
        MenuItem MapProduct(BsonDocument doc)
        {
            var optionGroups = doc.GetValue("OptionGroups", new BsonArray())
                .AsBsonArray
                .Select(group =>
                {
                    var bsonGroup = group.AsBsonDocument;
                    var choices = bsonGroup.GetValue("Choices", new BsonArray())
                        .AsBsonArray
                        .Select(choice =>
                        {
                            var bsonChoice = choice.AsBsonDocument;
                            return new OptionChoice(
                                Id: bsonChoice["Id"].AsString,
                                Name: bsonChoice["Name"].AsString,
                                ExtraPrice: MongoUtils.ToBsonDecimal(bsonChoice["ExtraPrice"]),
                                Available: bsonChoice.GetValue("Available", true).ToBoolean());
                        })
                        .ToImmutableArray();

                    return new OptionGroup(
                        Id: bsonGroup["Id"].AsString,
                        Name: bsonGroup["Name"].AsString,
                        Minimum: bsonGroup["Minimum"].ToInt32(),
                        Maximum: bsonGroup["Maximum"].ToInt32(),
                        Choices: choices);
                })
                .ToImmutableArray();

            var item = new MenuItem(
                Id: doc["Id"].AsString,
                Name: doc["Name"].AsString,
                Category: doc["Category"].AsString,
                BasePrice: MongoUtils.ToBsonDecimal(doc["BasePrice"]),
                Available: doc["Available"].AsBoolean,
                OptionGroups: optionGroups);

            return item with
            {
                ImagePath = doc.TryGetValue("ImagePath", out var imagePath) && imagePath.IsString
                    ? imagePath.AsString
                    : null,
                ImageData = doc.TryGetValue("ImageData", out var imageData) && imageData.IsString
                    ? imageData.AsString
                    : null
            };
        }

        Promotion MapPromotion(BsonDocument doc) => new Promotion(
            Code: doc["Code"].AsString,
            Description: doc["Description"].AsString,
            Kind: Enum.Parse<DiscountKind>(doc["Kind"].AsString, ignoreCase: true),
            Value: MongoUtils.ToBsonDecimal(doc["Value"]),
            MinimumSubtotal: MongoUtils.ToBsonDecimal(doc["MinimumSubtotal"]),
            StartsAt: DateTimeOffset.Parse(doc["StartsAt"].AsString),
            EndsAt: DateTimeOffset.Parse(doc["EndsAt"].AsString),
            Active: doc["Active"].AsBoolean,
            ProductIds: doc.GetValue("ProductIds", new BsonArray()).AsBsonArray
                .Where(value => value.IsString).Select(value => value.AsString).ToImmutableArray(),
            Categories: doc.GetValue("Categories", new BsonArray()).AsBsonArray
                .Where(value => value.IsString).Select(value => value.AsString).ToImmutableArray());

        var source = new MongoSalesDataSource(catalogCollection, promotionsCollection, MapProduct, MapPromotion, new DemoSessionProvider());

        // Use MongoDB for orders
        var ordersCollection = database.GetCollection<BsonDocument>("orders");
        var orderMapper = new CashierOrderMapper();
        var orders = new MongoOrderRepository(ordersCollection, orderMapper);
        var receiptHistory = new MongoReceiptHistory(ordersCollection, orderMapper);

        var journal = new JsonCheckoutJournal(Path.Combine(directory, "pending-checkout.json"));
        var controller = new CashierController(source, orders, journal);
        try { Application.Run(new SalesForm(controller, directory, receiptHistory)); }
        catch (Exception e)
        {
            MessageBox.Show(e.Message, "Không mở được Cashier", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}

