using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Cashier.Core;
using Manager.Core;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Manager.Infrastructure;

public sealed class MongoCatalogStore : ICatalogStore
{
    private readonly IMongoCollection<BsonDocument> _catalog;
    private readonly IMongoCollection<BsonDocument> _promotions;
    private readonly IMongoCollection<BsonDocument> _categories;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public MongoCatalogStore(IMongoDatabase database)
    {
        _catalog = database.GetCollection<BsonDocument>("catalog");
        _promotions = database.GetCollection<BsonDocument>("promotions");
        _categories = database.GetCollection<BsonDocument>("categories");
    }

    public async Task<ImmutableArray<MenuItem>> GetMenuAsync(
        CancellationToken token = default)
    {
        await _gate.WaitAsync(token);

        try
        {
            var documents = await _catalog
                .Find(Builders<BsonDocument>.Filter.Empty)
                .ToListAsync(token);

            return documents
                .Select(ToMenuItem)
                .ToImmutableArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveMenuAsync(
        ImmutableArray<MenuItem> menu,
        CancellationToken token = default)
    {
        await _gate.WaitAsync(token);

        try
        {
            foreach (var item in menu)
            {
                var filter = Builders<BsonDocument>.Filter.Or(
                    Builders<BsonDocument>.Filter.Eq("_id", item.Id),
                    Builders<BsonDocument>.Filter.Eq("Id", item.Id));
                var document = ToMenuDocument(item);

                var existing = await _catalog.Find(filter).FirstOrDefaultAsync(token);
                if (existing is null)
                {
                    await _catalog.ReplaceOneAsync(
                        Builders<BsonDocument>.Filter.Eq("_id", item.Id),
                        document,
                        new ReplaceOptions { IsUpsert = true },
                        token);
                    continue;
                }

                // Keep a legacy/imported _id intact; MongoDB does not allow replacing it.
                document.Remove("_id");
                var updates = new List<UpdateDefinition<BsonDocument>>
                {
                    new BsonDocumentUpdateDefinition<BsonDocument>(
                        new BsonDocument("$set", document))
                };
                if (!document.Contains("ImagePath"))
                {
                    updates.Add(new BsonDocumentUpdateDefinition<BsonDocument>(
                        new BsonDocument("$unset", new BsonDocument("ImagePath", ""))));
                }
                if (!document.Contains("ImageData"))
                {
                    updates.Add(new BsonDocumentUpdateDefinition<BsonDocument>(
                        new BsonDocument("$unset", new BsonDocument("ImageData", ""))));
                }

                await _catalog.UpdateManyAsync(filter,
                    Builders<BsonDocument>.Update.Combine(updates),
                    options: null,
                    cancellationToken: token);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteMenuItemAsync(string itemId, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);

        try
        {
            // Imported catalog documents can have an ObjectId in `_id` while the
            // application's stable product key is stored in `Id`. Match both
            // shapes, and remove duplicate legacy/imported documents together.
            var filter = Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Eq("_id", itemId),
                Builders<BsonDocument>.Filter.Eq("Id", itemId));
            var result = await _catalog.DeleteManyAsync(filter, token);
            if (result.DeletedCount == 0)
            {
                throw new InvalidOperationException($"Không tìm thấy sản phẩm '{itemId}' trong MongoDB catalog.");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ImmutableArray<string>> GetCategoriesAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            var products = await _catalog.Find(Builders<BsonDocument>.Filter.Empty).ToListAsync(token);
            var productCategories = products
                .Where(doc => doc.TryGetValue("Category", out var value) && value.IsString)
                .Select(doc => doc["Category"].AsString.Trim())
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var name in productCategories)
            {
                var filter = Builders<BsonDocument>.Filter.Regex("Name",
                    new BsonRegularExpression("^" + Regex.Escape(name) + "$", "i"));
                var exists = await _categories.Find(filter).AnyAsync(token);
                if (!exists)
                {
                    await _categories.ReplaceOneAsync(
                        Builders<BsonDocument>.Filter.Eq("_id", name),
                        new BsonDocument { { "_id", name }, { "Name", name } },
                        new ReplaceOptions { IsUpsert = true }, token);
                }
            }

            var stored = await _categories.Find(Builders<BsonDocument>.Filter.Empty).ToListAsync(token);
            return stored
                .Select(doc => doc.GetValue("Name", doc.GetValue("_id", BsonNull.Value)))
                .Where(value => value.IsString)
                .Select(value => value.AsString.Trim())
                .Concat(productCategories)
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .ToImmutableArray();
        }
        finally { _gate.Release(); }
    }

    public async Task CreateCategoryAsync(string category, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            await _categories.InsertOneAsync(new BsonDocument { { "_id", category }, { "Name", category } }, cancellationToken: token);
        }
        finally { _gate.Release(); }
    }

    public async Task RenameCategoryAsync(string oldName, string newName, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            var oldRegex = new BsonRegularExpression("^" + Regex.Escape(oldName) + "$", "i");
            await _catalog.UpdateManyAsync(
                Builders<BsonDocument>.Filter.Regex("Category", oldRegex),
                Builders<BsonDocument>.Update.Set("Category", newName), cancellationToken: token);

            var promotionDocs = await _promotions.Find(Builders<BsonDocument>.Filter.Empty).ToListAsync(token);
            foreach (var doc in promotionDocs)
            {
                var categories = ReadStringArray(doc, "Categories");
                if (!categories.Any(value => value.Equals(oldName, StringComparison.OrdinalIgnoreCase))) continue;
                var renamed = categories.Select(value => value.Equals(oldName, StringComparison.OrdinalIgnoreCase) ? newName : value)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(value => (BsonValue)new BsonString(value));
                await _promotions.UpdateOneAsync(
                    Builders<BsonDocument>.Filter.Eq("_id", doc["_id"]),
                    Builders<BsonDocument>.Update.Set("Categories", new BsonArray(renamed)), cancellationToken: token);
            }

            await _categories.DeleteManyAsync(Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Eq("_id", oldName),
                Builders<BsonDocument>.Filter.Regex("Name", oldRegex)), token);
            await _categories.ReplaceOneAsync(Builders<BsonDocument>.Filter.Eq("_id", newName),
                new BsonDocument { { "_id", newName }, { "Name", newName } },
                new ReplaceOptions { IsUpsert = true }, token);
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteCategoryAsync(string category, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            var regex = new BsonRegularExpression("^" + Regex.Escape(category) + "$", "i");
            await _categories.DeleteManyAsync(Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Eq("_id", category),
                Builders<BsonDocument>.Filter.Regex("Name", regex)), token);
        }
        finally { _gate.Release(); }
    }

    public async Task<ImmutableArray<Promotion>> GetPromotionsAsync(
        CancellationToken token = default)
    {
        await _gate.WaitAsync(token);

        try
        {
            var documents = await _promotions
                .Find(Builders<BsonDocument>.Filter.Empty)
                .ToListAsync(token);

            return documents
                .Select(ToPromotion)
                .ToImmutableArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SavePromotionsAsync(
        ImmutableArray<Promotion> promotions,
        CancellationToken token = default)
    {
        await _gate.WaitAsync(token);

        try
        {
            foreach (var promo in promotions)
            {
                var filter = Builders<BsonDocument>.Filter.Eq("_id", promo.Code);
                var document = ToPromotionDocument(promo);

                await _promotions.ReplaceOneAsync(
                    filter,
                    document,
                    new ReplaceOptions { IsUpsert = true },
                    token);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static MenuItem ToMenuItem(BsonDocument document)
    {
        var optionGroups = document.GetValue("OptionGroups", new BsonArray())
            .AsBsonArray
            .Select(og => ToOptionGroup(og.AsBsonDocument))
            .ToImmutableArray();

        var menuItem = new MenuItem(
            Id: document["Id"].AsString,
            Name: document["Name"].AsString,
            Category: document["Category"].AsString,
            BasePrice: ToDecimal(document["BasePrice"]),
            Available: document["Available"].AsBoolean,
            OptionGroups: optionGroups);

        if (document.TryGetValue("ImagePath", out var imagePath) && imagePath.IsString)
        {
            menuItem = menuItem with { ImagePath = imagePath.AsString };
        }
        if (document.TryGetValue("ImageData", out var imageData) && imageData.IsString)
        {
            menuItem = menuItem with { ImageData = imageData.AsString };
        }

        return menuItem;
    }

    private static OptionGroup ToOptionGroup(BsonDocument document)
    {
        var choices = document["Choices"]
            .AsBsonArray
            .Select(c => ToOptionChoice(c.AsBsonDocument))
            .ToImmutableArray();

        return new OptionGroup(
            Id: document["Id"].AsString,
            Name: document["Name"].AsString,
            Minimum: document["Minimum"].AsInt32,
            Maximum: document["Maximum"].AsInt32,
            Choices: choices);
    }

    private static OptionChoice ToOptionChoice(BsonDocument document)
    {
        return new OptionChoice(
            Id: document["Id"].AsString,
            Name: document["Name"].AsString,
            ExtraPrice: ToDecimal(document["ExtraPrice"]),
            Available: document.GetValue("Available", true).AsBoolean);
    }

    private static Promotion ToPromotion(BsonDocument document)
    {
        return new Promotion(
            Code: document["Code"].AsString,
            Description: document["Description"].AsString,
            Kind: Enum.Parse<DiscountKind>(
                document["Kind"].AsString,
                ignoreCase: true),
            Value: ToDecimal(document["Value"]),
            MinimumSubtotal: ToDecimal(document["MinimumSubtotal"]),
            StartsAt: DateTimeOffset.Parse(document["StartsAt"].AsString),
            EndsAt: DateTimeOffset.Parse(document["EndsAt"].AsString),
            Active: document["Active"].AsBoolean,
            ProductIds: ReadStringArray(document, "ProductIds"),
            Categories: ReadStringArray(document, "Categories"));
    }

    private static BsonDocument ToMenuDocument(MenuItem item)
    {
        var doc = new BsonDocument
        {
            { "_id", item.Id },
            { "Id", item.Id },
            { "Name", item.Name },
            { "Category", item.Category },
            { "BasePrice", item.BasePrice },
            { "Available", item.Available },
            { "OptionGroups", new BsonArray(
                item.OptionGroups.Select(og => ToOptionGroupDocument(og)))
            }
        };

        if (!string.IsNullOrEmpty(item.ImagePath))
        {
            doc["ImagePath"] = item.ImagePath;
        }
        if (!string.IsNullOrEmpty(item.ImageData))
        {
            doc["ImageData"] = item.ImageData;
        }

        return doc;
    }

    private static BsonDocument ToOptionGroupDocument(OptionGroup group)
    {
        return new BsonDocument
        {
            { "Id", group.Id },
            { "Name", group.Name },
            { "Minimum", group.Minimum },
            { "Maximum", group.Maximum },
            { "Choices", new BsonArray(
                group.Choices.Select(c => ToOptionChoiceDocument(c)))
            }
        };
    }

    private static BsonDocument ToOptionChoiceDocument(OptionChoice choice)
    {
        return new BsonDocument
        {
            { "Id", choice.Id },
            { "Name", choice.Name },
            { "ExtraPrice", choice.ExtraPrice },
            { "Available", choice.Available }
        };
    }

    private static BsonDocument ToPromotionDocument(Promotion promo)
    {
        return new BsonDocument
        {
            { "_id", promo.Code },
            { "Code", promo.Code },
            { "Description", promo.Description },
            { "Kind", promo.Kind.ToString() },
            { "Value", promo.Value },
            { "MinimumSubtotal", promo.MinimumSubtotal },
            { "StartsAt", promo.StartsAt.ToString("O") },
            { "EndsAt", promo.EndsAt.ToString("O") },
            { "Active", promo.Active },
            { "ProductIds", new BsonArray((promo.ProductIds.IsDefault
                ? ImmutableArray<string>.Empty
                : promo.ProductIds).Select(id => (BsonValue)new BsonString(id))) },
            { "Categories", new BsonArray((promo.Categories.IsDefault
                ? ImmutableArray<string>.Empty
                : promo.Categories).Select(category => (BsonValue)new BsonString(category))) }
        };
    }

    private static ImmutableArray<string> ReadStringArray(BsonDocument document, string field) =>
        document.TryGetValue(field, out var value) && value.IsBsonArray
            ? value.AsBsonArray.Where(item => item.IsString).Select(item => item.AsString).ToImmutableArray()
            : [];

    private static decimal ToDecimal(BsonValue value)
    {
        return value.BsonType switch
        {
            BsonType.Int32 => value.AsInt32,
            BsonType.Int64 => value.AsInt64,
            BsonType.Double => (decimal)value.AsDouble,
            BsonType.Decimal128 => (decimal)value.AsDecimal,
            _ => throw new InvalidOperationException($"Cannot convert {value.BsonType} to decimal")
        };
    }
}
