using Microsoft.AspNetCore.Mvc.Filters;
using Travela.Api.Controllers;

namespace Travela.Api.Trips;

// Refresh before reads as well, so filtering, counts and ticket details use the same status
// at the exact departure/arrival boundary rather than waiting for the background interval.
public sealed class TripLifecycleFilter(TripLifecycleService lifecycle) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (HttpMethods.IsGet(context.HttpContext.Request.Method) && context.Controller is
            TripsController or CompanyReportsController or CompanyAttentionController or BusController or
            Travela.API.Controllers.CompanyController or Travela.API.Controllers.ProfileController or ReservationsController or TicketsController)
            await lifecycle.Synchronize(context.HttpContext.RequestAborted);
        await next();
    }
}
