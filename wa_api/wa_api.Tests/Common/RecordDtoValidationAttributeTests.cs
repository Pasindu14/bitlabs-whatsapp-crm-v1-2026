using System.ComponentModel.DataAnnotations;
using System.Reflection;
using wa_api.Features.Bot.Dtos;
using Xunit;

namespace wa_api.Tests.Common;

/// <summary>
/// Guards against a whole class of bug that is invisible until a real request reaches the endpoint.
/// <para>
/// On a positional record, <c>[property: Required]</c> puts the attribute on the generated property but
/// NOT on the constructor parameter. ASP.NET Core's
/// <c>ModelMetadata.ThrowIfRecordTypeHasValidationOnProperties</c> refuses to bind such a type and
/// throws <see cref="InvalidOperationException"/> — so the endpoint returns 500 for every request,
/// while the project still compiles, every unit test still passes, and Swagger still renders it.
/// </para>
/// <para>
/// This shipped undetected on <c>CreateAutoReplyRuleRequest</c> and was reintroduced on the bot
/// profile, preview and pause DTOs. One reflection test over the whole assembly is cheaper than
/// discovering it once per endpoint in production.
/// </para>
/// </summary>
public class RecordDtoValidationAttributeTests
{
    [Fact]
    public void NoRecordDto_PutsValidationAttributesOnPropertiesInsteadOfConstructorParameters()
    {
        var assembly = typeof(AutoReplyRuleResponse).Assembly;
        var offenders = new List<string>();

        foreach (var type in assembly.GetTypes().Where(IsPositionalRecord))
        {
            var primary = PrimaryConstructor(type);
            if (primary is null) continue;

            var parameterNames = primary.GetParameters()
                .Select(p => p.Name)
                .Where(n => n is not null)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                // Only properties that ALSO exist as a primary-constructor parameter are affected.
                if (!parameterNames.Contains(property.Name)) continue;

                var onProperty = property.GetCustomAttributes<ValidationAttribute>(inherit: true).ToList();
                if (onProperty.Count == 0) continue;

                var parameter = primary.GetParameters()
                    .First(p => string.Equals(p.Name, property.Name, StringComparison.Ordinal));

                // The attribute is fine as long as the PARAMETER carries it too — which is what
                // omitting the `property:` target achieves.
                if (parameter.GetCustomAttributes<ValidationAttribute>(inherit: true).Any()) continue;

                offenders.Add(
                    $"{type.FullName}.{property.Name} "
                    + $"[{string.Join(", ", onProperty.Select(a => a.GetType().Name))}]");
            }
        }

        Assert.True(offenders.Count == 0,
            "These record DTOs carry validation attributes on the property rather than the constructor "
            + "parameter. ASP.NET Core throws InvalidOperationException when binding them, so every "
            + "request to the endpoint returns 500. Drop the `property:` target:\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>A record whose members came from a primary constructor.</summary>
    private static bool IsPositionalRecord(Type type) =>
        type is { IsClass: true, IsPublic: true }
        && type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) is not null
        && PrimaryConstructor(type) is not null;

    /// <summary>
    /// The record's primary constructor: the one whose parameters all match a property by name. The
    /// copy constructor takes the record type itself, so it is excluded by that rule.
    /// </summary>
    private static ConstructorInfo? PrimaryConstructor(Type type)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        return type.GetConstructors()
            .Where(c => c.GetParameters().Length > 0)
            .Where(c => c.GetParameters().All(p => p.Name is not null && properties.Contains(p.Name)))
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault();
    }
}
