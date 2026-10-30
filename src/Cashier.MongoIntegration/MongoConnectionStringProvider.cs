namespace Cashier.MongoIntegration;

public static class MongoConnectionStringProvider
{
    public static string Resolve()
    {
        var configuredUri = Environment.GetEnvironmentVariable("POS_MONGODB_URI");
        if (!string.IsNullOrWhiteSpace(configuredUri))
        {
            return configuredUri;
        }

        var credentialsPath = Path.Combine(AppContext.BaseDirectory, "atlas-credentials.env");
        if (File.Exists(credentialsPath))
        {
            var uriLine = File.ReadLines(credentialsPath)
                .Select(line => line.Trim())
                .FirstOrDefault(line => line.StartsWith("MONGODB_URI=", StringComparison.Ordinal));

            if (uriLine is not null)
            {
                var uri = uriLine["MONGODB_URI=".Length..].Trim().Trim('"', '\'');
                if (!string.IsNullOrWhiteSpace(uri))
                {
                    return uri;
                }
            }
        }

        return "mongodb://127.0.0.1:27017";
    }
}
