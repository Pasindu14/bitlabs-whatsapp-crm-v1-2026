using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace wa_api.Common.Extensions;

/// <summary>
/// Applies a global route prefix (e.g. <c>api/v1</c>) to every attribute-routed
/// controller so feature controllers declare only their own relative route
/// (<c>[Route("contacts")]</c> → <c>/api/v1/contacts</c>).
/// </summary>
public class RoutePrefixConvention(string prefix) : IApplicationModelConvention
{
    private readonly AttributeRouteModel _routePrefix = new(new RouteAttribute(prefix));

    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers)
        {
            foreach (var selector in controller.Selectors)
            {
                selector.AttributeRouteModel = selector.AttributeRouteModel is null
                    ? _routePrefix
                    : AttributeRouteModel.CombineAttributeRouteModel(_routePrefix, selector.AttributeRouteModel);
            }
        }
    }
}
