using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;

namespace WebApi.ModelBinding;

public sealed class DateOnlyModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Metadata.UnderlyingOrModelType == typeof(DateOnly)
            ? new BinderTypeModelBinder(typeof(DateOnlyModelBinder))
            : null;
    }
}
