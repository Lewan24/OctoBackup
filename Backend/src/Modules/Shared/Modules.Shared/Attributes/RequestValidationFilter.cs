using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Modules.Shared.Attributes;

public sealed class RequestValidationFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext c, EndpointFilterDelegate next)
    {
        foreach (var argument in c.Arguments)
        {
            if (argument is null || argument.GetType().Namespace?.StartsWith("Modules.") != true) continue;
            var errors = new List<ValidationResult>();
            if (!Validator.TryValidateObject(argument, new ValidationContext(argument), errors, true))
                return ValueTask.FromResult<object?>(Results.BadRequest(string.Join(" ", errors.Select(x => x.ErrorMessage))));
        }
        return next(c);
    }
}
