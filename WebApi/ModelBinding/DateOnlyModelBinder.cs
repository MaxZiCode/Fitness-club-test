using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace WebApi.ModelBinding;

public sealed class DateOnlyModelBinder : IModelBinder
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

        if (DateOnly.TryParseExact(
                input,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var value))
        {
            bindingContext.Result = ModelBindingResult.Success(value);
        }
        else
        {
            bindingContext.ModelState.TryAddModelError(
                bindingContext.ModelName,
                $"The value '{input}' is not a valid date. Expected format is yyyy-MM-dd without a time component.");
        }

        return Task.CompletedTask;
    }
}
