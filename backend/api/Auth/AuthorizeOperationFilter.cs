using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Reviews.Api.Auth;

// Tags every [Authorize]'d operation in the spec with the Bearer scheme so
// Swagger UI shows a lock icon and the generated TS client knows the endpoint
// expects a token. Operations explicitly marked [AllowAnonymous] (the public
// product reads, /config, the GET on /api/images/{path}) stay unsecured.
public class AuthorizeOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var methodInfo = context.MethodInfo;
        var hasAuthorize = methodInfo.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any()
                           || methodInfo.DeclaringType?.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any() == true;

        var hasAllowAnonymous = methodInfo.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();

        if (!hasAuthorize || hasAllowAnonymous)
            return;

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = ["Bearer"],
        });
    }
}
