using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StockQuoteAlert.Infrastructure;

namespace StockQuoteAlert.Tests.Infrastructure;

public sealed class AddValidatedOptionsTests
{
    [Fact]
    public void BindsTheConfigurationSection()
    {
        using var services = BuildProvider(new() { ["Sample:Name"] = "bound", ["Other:Name"] = "ignored" });

        Assert.Equal("bound", services.GetRequiredService<IOptions<SampleOptions>>().Value.Name);
    }

    [Fact]
    public void WithoutTheSection_KeepsDefaults()
    {
        using var services = BuildProvider([]);

        Assert.Equal("default", services.GetRequiredService<IOptions<SampleOptions>>().Value.Name);
    }

    [Fact]
    public void ValidatesDataAnnotations()
    {
        using var services = BuildProvider(new() { ["Sample:Name"] = "" });

        var exception = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<SampleOptions>>().Value);

        Assert.Contains(exception.Failures, failure => failure.Contains("Sample:Name is required.", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatesIValidatableObject()
    {
        using var services = BuildProvider(new() { ["Sample:Name"] = "forbidden" });

        var exception = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<SampleOptions>>().Value);

        Assert.Contains(exception.Failures, failure => failure.Contains("Sample:Name cannot be 'forbidden'.", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatesOnStart()
    {
        using var services = BuildProvider(new() { ["Sample:Name"] = "" });

        var exception = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(exception.Failures, failure => failure.Contains("Sample:Name is required.", StringComparison.Ordinal));
    }

    [Fact]
    public void WithValidConfiguration_StartsWithoutErrors()
    {
        using var services = BuildProvider(new() { ["Sample:Name"] = "valid" });

        services.GetRequiredService<IStartupValidator>().Validate();
    }

    [Fact]
    public void ReturnsTheSameCollection()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddValidatedOptions<SampleOptions>("Sample"));
    }

    [Fact]
    public void WithNullServices_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => DependencyInjection.AddValidatedOptions<SampleOptions>(null!, "Sample"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void WithoutSectionName_Throws(string? sectionName)
    {
        Assert.ThrowsAny<ArgumentException>(() => new ServiceCollection().AddValidatedOptions<SampleOptions>(sectionName!));
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        return new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddValidatedOptions<SampleOptions>("Sample")
            .BuildServiceProvider();
    }

    private sealed class SampleOptions : IValidatableObject
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "Sample:Name is required.")]
        public string Name { get; set; } = "default";

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (Name == "forbidden")
            {
                yield return new ValidationResult("Sample:Name cannot be 'forbidden'.", [nameof(Name)]);
            }
        }
    }
}