using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace WebApi.ModelBinding;

public sealed class UtcDateTimeModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var valueResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (valueResult == ValueProviderResult.None)
            return Task.CompletedTask;

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, valueResult);
        var input = valueResult.FirstValue;
        if (string.IsNullOrWhiteSpace(input))
            return Task.CompletedTask;

        if (DateTimeOffset.TryParse(
                input,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var value))
        {
            bindingContext.Result = ModelBindingResult.Success(value.UtcDateTime);
        }
        else
        {
            bindingContext.ModelState.TryAddModelError(
                bindingContext.ModelName,
                $"The value '{input}' is not a valid date and time.");
        }

        return Task.CompletedTask;
    }
}
