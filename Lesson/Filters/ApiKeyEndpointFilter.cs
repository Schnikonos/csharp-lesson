namespace Lesson.Filters;

/// <summary>
/// Lesson 06-C — IEndpointFilter (.NET 7+ minimal-API style).
///
/// IEndpointFilter is the minimal-API equivalent of IActionFilter.
/// It intercepts route handler invocations (MapGet/MapPost/etc.) rather than
/// controller actions.
///
/// This implementation validates that a required query parameter "apiKey" is
/// present; otherwise it short-circuits with 401 Unauthorized.
///
/// Java parallel: OncePerRequestFilter on a specific servlet path — return
/// 401 without calling filterChain.doFilter() when the key is missing.
/// </summary>
public class ApiKeyEndpointFilter(string requiredKey) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var provided = context.HttpContext.Request.Query["apiKey"].ToString();

        if (provided != requiredKey)
        {
            return Results.Unauthorized();
        }

        return await next(context);
    }
}
