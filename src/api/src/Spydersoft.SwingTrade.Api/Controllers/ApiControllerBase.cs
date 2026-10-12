using Microsoft.AspNetCore.Mvc;
using Spydersoft.SwingTrade.Api.Services;

namespace Spydersoft.SwingTrade.Api.Controllers;

/// <summary>Maps service outcomes to HTTP: 404, 400 ValidationProblemDetails, 409 ProblemDetails.</summary>
public abstract class ApiControllerBase : ControllerBase
{
    protected ActionResult<T> Respond<T>(ServiceResult<T> result) => Respond(result, value => Ok(value));

    protected ActionResult<T> Respond<T>(ServiceResult<T> result, Func<T, ActionResult> onOk)
    {
        ActionResult mapped = result.Kind switch
        {
            ResultKind.Ok => onOk(result.Value!),
            ResultKind.NotFound => NotFound(),
            ResultKind.Invalid => ValidationProblem(new ValidationProblemDetails(result.Errors) { Status = StatusCodes.Status400BadRequest }),
            ResultKind.Conflict => Problem(detail: result.Detail, statusCode: StatusCodes.Status409Conflict, title: "Conflict"),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
        return mapped;
    }
}
