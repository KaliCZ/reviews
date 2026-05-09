using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Reviews.Api.Auth;

// Promotes every non-nullable record/class property to the schema's `required`
// list. `SupportNonNullableReferenceTypes()` populates the per-property
// `nullable` flag from C#'s nullable annotations, but Swashbuckle's
// positional-record inspector doesn't always carry that through to
// `required`. Generated TS clients then emit every field as `?: T | undefined`,
// which loses the StrongTypes contract.
//
// We re-derive nullability from the underlying CLR type via the
// NullabilityInfoContext API rather than re-parsing the in-progress schema.
public class RequireNonNullableSchemaFilter : ISchemaFilter
{
    private static readonly NullabilityInfoContext NullabilityCtx = new();

    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema is not OpenApiSchema concrete || concrete.Properties is null)
            return;

        var clrType = context.Type;
        if (clrType is null)
            return;

        // Match openapi member names by lowerCamelCase, the casing Swashbuckle
        // emits by default. The reflection lookup uses the underlying PascalCase
        // CLR member, so we map back through a name-insensitive comparison.
        var members = clrType
            .GetMembers(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => m is PropertyInfo or FieldInfo)
            .ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);

        concrete.Required ??= new HashSet<string>();
        foreach (var (propertyName, _) in concrete.Properties)
        {
            if (!members.TryGetValue(propertyName, out var member))
                continue;

            var nullability = member switch
            {
                PropertyInfo p => NullabilityCtx.Create(p),
                FieldInfo f => NullabilityCtx.Create(f),
                _ => null,
            };
            if (nullability is null) continue;

            // ReadState is the right axis for "the API will emit this": if
            // the property type isn't nullable when read, the field is always
            // present in the response and required in any inbound payload.
            if (nullability.ReadState == NullabilityState.NotNull)
                concrete.Required.Add(propertyName);
        }
    }
}
