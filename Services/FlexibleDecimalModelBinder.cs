using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Kumburgaz.Web.Services;

/// <summary>
/// decimal / decimal? form ve sorgu alanlarını <see cref="FlexibleDecimalParser"/> ile bağlar.
/// Varsayılan binder tr-TR kültürüyle çalıştığı için "1234.56" girişini 123456 yapıyordu;
/// istemci tarafı doğrulama (site.js) hem virgülü hem noktayı kabul ettiği için hata sessizdi.
/// </summary>
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

        var raw = valueResult.FirstValue;
        var isNullable = Nullable.GetUnderlyingType(bindingContext.ModelType) is not null;
        var messages = bindingContext.ModelMetadata.ModelBindingMessageProvider;

        if (string.IsNullOrWhiteSpace(raw))
        {
            if (isNullable)
            {
                bindingContext.Result = ModelBindingResult.Success(null);
            }
            else
            {
                bindingContext.ModelState.TryAddModelError(
                    bindingContext.ModelName,
                    messages.ValueMustNotBeNullAccessor(raw ?? string.Empty));
            }

            return Task.CompletedTask;
        }

        if (FlexibleDecimalParser.TryParse(raw, out var amount))
        {
            bindingContext.Result = ModelBindingResult.Success(amount);
        }
        else
        {
            bindingContext.ModelState.TryAddModelError(
                bindingContext.ModelName,
                messages.AttemptedValueIsInvalidAccessor(raw, bindingContext.ModelMetadata.GetDisplayName()));
        }

        return Task.CompletedTask;
    }
}

public sealed class FlexibleDecimalModelBinderProvider : IModelBinderProvider
{
    private static readonly FlexibleDecimalModelBinder Binder = new();

    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        var type = context.Metadata.ModelType;
        return type == typeof(decimal) || type == typeof(decimal?) ? Binder : null;
    }
}

public static class FlexibleDecimalBindingExtensions
{
    public static MvcOptions UseFlexibleDecimalBinding(this MvcOptions options)
    {
        options.ModelBinderProviders.Insert(0, new FlexibleDecimalModelBinderProvider());
        return options;
    }
}
