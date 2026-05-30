namespace Lesson.Configuration;

/// <summary>
/// Lesson 02-C — Custom IConfigurationProvider
///
/// Demonstrates how to pull configuration from any source (in a real app this
/// would be a database, HTTP endpoint, Azure Key Vault custom loader, etc.).
///
/// Two classes are required:
///   IConfigurationSource  — describes how to build the provider; added to
///                           IConfigurationBuilder via builder.Configuration.Add(source).
///   ConfigurationProvider — does the actual loading; inherits the base class
///                           which stores values in Data (Dictionary&lt;string,string?&gt;).
///
/// Java parallel: implement PropertySource&lt;T&gt; and register it on
///   ConfigurableEnvironment.getPropertySources().
/// </summary>

// ─────────────────────────────────────────────────────────────────────────────
// Source: factory that the framework calls to create the provider
// ─────────────────────────────────────────────────────────────────────────────
public sealed class InMemoryDbConfigurationSource : IConfigurationSource
{
    /// <summary>Key/value pairs that simulate a database config table.</summary>
    public Dictionary<string, string?> Data { get; set; } = new();

    public IConfigurationProvider Build(IConfigurationBuilder builder)
        => new InMemoryDbConfigurationProvider(Data);
}

// ─────────────────────────────────────────────────────────────────────────────
// Provider: loads data into the inherited Data dictionary
// ─────────────────────────────────────────────────────────────────────────────
public sealed class InMemoryDbConfigurationProvider(Dictionary<string, string?> data)
    : ConfigurationProvider
{
    public override void Load()
    {
        // In a real implementation: query a DB, call an HTTP endpoint, decrypt a file…
        // Here we just copy the seed data supplied by the source.
        Data = data;
    }
}
