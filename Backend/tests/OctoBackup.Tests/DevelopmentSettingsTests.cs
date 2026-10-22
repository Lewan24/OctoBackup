using Microsoft.Extensions.Configuration;
using Server.Api.Configuration;
using Xunit;

public sealed class DevelopmentSettingsTests
{
    private static (string Directory, ConfigurationManager Configuration) Settings()
    {
        var directory = Path.Combine(Path.GetTempPath(), "octobackup-config-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "appsettings.json"), """
            {"CRYPTO_KEY":null,"Bootstrap":{"AdminEmail":"","AdminPassword":""},
             "Storage":{"DataDirectory":"data"},"Development":{"AutoProvisionLocalSettings":true}}
            """);
        var configuration = new ConfigurationManager();
        configuration.AddJsonFile(Path.Combine(directory, "appsettings.json"));
        return (directory, configuration);
    }

    [Fact]
    public void FreshDevelopmentStart_PersistsPrivateKeyAndBootstrap_AndReusesThem()
    {
        var (directory, configuration) = Settings();
        Assert.True(DevelopmentSettings.Configure(configuration, directory, true));
        var key = configuration["CRYPTO_KEY"];
        Assert.Equal(32, Convert.FromBase64String(key!).Length);
        Assert.False(string.IsNullOrWhiteSpace(configuration["Bootstrap:AdminPassword"]));
        var original = File.ReadAllText(Path.Combine(directory, DevelopmentSettings.FileName));
        Assert.False(DevelopmentSettings.Configure(configuration, directory, true));
        Assert.Equal(key, configuration["CRYPTO_KEY"]);
        Assert.Equal(original, File.ReadAllText(Path.Combine(directory, DevelopmentSettings.FileName)));
        Assert.Equal(Path.Combine(directory, "data"), DevelopmentSettings.ResolveDataDirectory(configuration, directory));
    }

    [Fact]
    public void Production_DoesNotGenerateOrLoadDevelopmentSecrets()
    {
        var (directory, configuration) = Settings();
        File.WriteAllText(Path.Combine(directory, DevelopmentSettings.FileName), "{\"CRYPTO_KEY\":\"development-only\"}");
        Assert.False(DevelopmentSettings.Configure(configuration, directory, false));
        Assert.Null(configuration["CRYPTO_KEY"]);
    }

    [Fact]
    public void ExplicitConfiguration_OverridesLocalJson_AndLegacyStorageOverrideWorks()
    {
        var (directory, configuration) = Settings();
        File.WriteAllText(Path.Combine(directory, DevelopmentSettings.FileName), "{\"CRYPTO_KEY\":\"local\"}");
        configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["CRYPTO_KEY"] = "explicit", ["OCTOBACKUP_DATA"] = "override-data" });
        DevelopmentSettings.Configure(configuration, directory, true);
        Assert.Equal("explicit", configuration["CRYPTO_KEY"]);
        Assert.Equal(Path.Combine(directory, "override-data"), DevelopmentSettings.ResolveDataDirectory(configuration, directory));
    }

    [Fact]
    public void MissingKeyForExistingData_StopsRatherThanSilentlyReplacingEncryptionKey()
    {
        var (directory, configuration) = Settings();
        Directory.CreateDirectory(Path.Combine(directory, "data", "Config"));
        File.WriteAllText(Path.Combine(directory, "data", "Config", "OctoBackup.db"), "existing-data");
        Assert.Throws<InvalidOperationException>(() => DevelopmentSettings.Configure(configuration, directory, true));
        Assert.False(File.Exists(Path.Combine(directory, DevelopmentSettings.FileName)));
    }

    [Fact]
    public void RepositoryEnvironment_IsImportedWithoutOverwritingOrChangingItsKey()
    {
        var (directory, configuration) = Settings();
        File.WriteAllText(Path.Combine(directory, "OctoBackup.sln"), "test marker");
        var key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        File.WriteAllText(Path.Combine(directory, ".env"), $"CRYPTO_KEY={key}\nADMIN_EMAIL=existing@example.com\nADMIN_PASSWORD='ExistingPrivate123!'\n");
        DevelopmentSettings.Configure(configuration, directory, true);
        Assert.Equal(key, configuration["CRYPTO_KEY"]);
        Assert.Equal("existing@example.com", configuration["Bootstrap:AdminEmail"]);
        Assert.Equal("ExistingPrivate123!", configuration["Bootstrap:AdminPassword"]);
    }
}
