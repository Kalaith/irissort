using System.Text.Json;

namespace IrisSort.Services.Configuration;

/// <summary>
/// Service for persisting and loading application configuration.
/// </summary>
public class ConfigurationService
{
    private static readonly string ConfigDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "IrisSort"
    );

    private static readonly string ConfigFilePath = Path.Combine(ConfigDirectory, "config.json");

    /// <summary>
    /// Persistable configuration data.
    /// </summary>
    public class IrisSortConfig
    {
        /// <summary>
        /// Base URL for LM Studio API.
        /// </summary>
        public string BaseUrl { get; set; } = "http://127.0.0.1:1234/v1";

        /// <summary>
        /// Selected model for vision analysis.
        /// </summary>
        public string Model { get; set; } = "zai-org/glm-4.6v-flash";

        /// <summary>
        /// Maximum dimension for images.
        /// </summary>
        public int MaxImageDimension { get; set; } = 1024;

        /// <summary>
        /// Temperature for generation.
        /// </summary>
        public double Temperature { get; set; } = 0.2;

        /// <summary>
        /// Maximum tokens to generate.
        /// </summary>
        public int MaxTokens { get; set; } = 1024;

        /// <summary>
        /// Request timeout in seconds.
        /// </summary>
        public int TimeoutSeconds { get; set; } = 120;

        /// <summary>
        /// Maximum retry attempts.
        /// </summary>
        public int MaxRetries { get; set; } = 3;
    }

    /// <summary>
    /// Loads configuration from disk. Creates default if not found.
    /// </summary>
    public static IrisSortConfig LoadConfiguration()
    {
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                var json = File.ReadAllText(ConfigFilePath);
                var config = JsonSerializer.Deserialize<IrisSortConfig>(json);
                return config ?? new IrisSortConfig();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load configuration: {ex.Message}");
        }

        return new IrisSortConfig();
    }

    /// <summary>
    /// Saves configuration to disk.
    /// </summary>
    public static void SaveConfiguration(IrisSortConfig config)
    {
        try
        {
            // Ensure directory exists
            Directory.CreateDirectory(ConfigDirectory);

            // Serialize to JSON with indentation for readability
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            var json = JsonSerializer.Serialize(config, options);
            File.WriteAllText(ConfigFilePath, json);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to save configuration: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Copies settings from IrisSortConfig to LmStudioConfiguration.
    /// </summary>
    public static void ApplyToLmStudioConfiguration(IrisSortConfig source, LmStudioConfiguration target)
    {
        target.BaseUrl = source.BaseUrl;
        target.Model = source.Model;
        target.Temperature = source.Temperature;
        target.MaxTokens = source.MaxTokens;
        target.TimeoutSeconds = source.TimeoutSeconds;
        target.MaxRetries = source.MaxRetries;
        target.MaxImageDimension = source.MaxImageDimension;
    }

    /// <summary>
    /// Copies settings from LmStudioConfiguration to IrisSortConfig.
    /// </summary>
    public static IrisSortConfig CreateFromLmStudioConfiguration(LmStudioConfiguration config)
    {
        return new IrisSortConfig
        {
            BaseUrl = config.BaseUrl,
            Model = config.Model,
            Temperature = config.Temperature,
            MaxTokens = config.MaxTokens,
            TimeoutSeconds = config.TimeoutSeconds,
            MaxRetries = config.MaxRetries,
            MaxImageDimension = config.MaxImageDimension
        };
    }

    /// <summary>
    /// Gets the path to the configuration file.
    /// </summary>
    public static string GetConfigFilePath()
    {
        return ConfigFilePath;
    }
}
