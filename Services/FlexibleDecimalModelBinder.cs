using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Kumburgaz.Web.Services;

public sealed class FlexibleDecimalModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var valueResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (valueResult == ValueProviderResult.None)
        {
            return Task.CompletedTask;
        }

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, valueResult);
        var value = valueResult.FirstValue;
        if (string.IsNullOrWhiteSpace(value))
        {
            if (Nullable.GetUnderlyingType(bindingContext.ModelType) is not null)
            {
                bindingContext.Result = ModelBindingResult.Success(null);
            }

            return Task.CompletedTask;
        }

        if (FlexibleDecimalParser.TryParse(value, out var amount))
        {
            bindingContext.Result = ModelBindingResult.Success(amount);
            return Task.CompletedTask;
        }

        bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, "Geçerli bir tutar giriniz.");
        return Task.CompletedTask;
    }
}

public sealed class FlexibleDecimalModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        var modelType = context.Metadata.UnderlyingOrModelType;
        return modelType == typeof(decimal) ? new FlexibleDecimalModelBinder() : null;
    }
}
