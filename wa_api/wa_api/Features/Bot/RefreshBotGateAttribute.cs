using Microsoft.AspNetCore.Mvc.Filters;
using wa_api.Common.Tenancy;

namespace wa_api.Features.Bot;

/// <summary>
/// After any write on a chatbot settings endpoint, forgets the cached "is the bot live for this company"
/// answer so the change takes effect on the next inbound message rather than when the cache expires.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RefreshBotGateAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var executed = await next();

        if (HttpMethods.IsGet(context.HttpContext.Request.Method) || executed.Exception is not null)
            return;

        var services = context.HttpContext.RequestServices;
        if (services.GetRequiredService<ITenantContext>().CompanyId is { } companyId)
            services.GetRequiredService<IBotGate>().Forget(companyId);
    }
}
