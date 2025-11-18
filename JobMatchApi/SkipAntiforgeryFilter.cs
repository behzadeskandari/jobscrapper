using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Antiforgery;

public class SkipAntiforgeryFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // Skip anti-forgery validation for this endpoint (e.g., for Swagger testing)
        var antiforgery = httpContext.RequestServices.GetRequiredService<IAntiforgery>();
        antiforgery.ValidateRequestAsync(httpContext).Wait();  // This will no-op if token is missing for this endpoint

        // Or, to completely bypass, set a flag (custom logic)
        httpContext.Items["SkipAntiforgery"] = true;

        return next(context);
    }
}