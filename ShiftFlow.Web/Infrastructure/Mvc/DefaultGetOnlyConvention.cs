using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Routing;

namespace ShiftFlow.Web.Infrastructure.Mvc;

/// <summary>
/// Read-only actions (Index/Details/etc.) across the app rely on ASP.NET Core's default
/// convention-based routing and have no explicit [HttpGet], so by default they accept ANY
/// HTTP verb (PUT, DELETE, TRACE, or an invented one) identically to GET — unnecessary attack
/// surface, and inconsistent with the app's state-changing actions, which already correctly
/// reject GET via their own [HttpPost] attribute (FINDING-004 / WSTG-CONF-06).
///
/// Rather than hand-annotating every read-only action, this convention restricts any action
/// that has no HTTP-method-selector attribute of its own to GET/HEAD. Actions already carrying
/// [HttpGet], [HttpPost], [AcceptVerbs(...)], etc. are left untouched since they already define
/// their own <see cref="HttpMethodActionConstraint"/>.
/// </summary>
public sealed class DefaultGetOnlyConvention : IActionModelConvention
{
    private static readonly string[] GetOnly = ["GET", "HEAD"];

    public void Apply(ActionModel action)
    {
        foreach (var selector in action.Selectors)
        {
            var hasVerbConstraint = selector.ActionConstraints
                .OfType<HttpMethodActionConstraint>()
                .Any();
            if (hasVerbConstraint) continue;

            selector.ActionConstraints.Add(new HttpMethodActionConstraint(GetOnly));
            selector.EndpointMetadata.Add(new HttpMethodMetadata(GetOnly));
        }
    }
}
