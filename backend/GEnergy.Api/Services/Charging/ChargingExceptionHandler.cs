using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
namespace GEnergy.Api.Services.Charging;

public sealed class ChargingExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var status = exception is ChargingException domain ? domain.Status : exception is DbUpdateConcurrencyException ? 409 :
            (exception is SqlException sql && sql.Number is 1205 or 1222) || (exception is DbUpdateException { InnerException: SqlException inner } && inner.Number is 2601 or 2627 or 1205 or 1222) ? 409 : 0;
        if (status == 0) return false;
        await Results.Problem(statusCode: status, title: exception is ChargingException e ? e.Message : "Resource state changed. Refresh and retry the request.").ExecuteAsync(context); return true;
    }
}
