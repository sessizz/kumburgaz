using System.Globalization;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Kumburgaz.Web.Tests;

// Uygulama tr-TR ile çalışır; varsayılan MVC binder'ı "1234.56" girişini
// 123456 olarak bağlıyordu (nokta = binlik ayracı). Tüm decimal form alanları
// FlexibleDecimalParser ile okunmalı.
public class FlexibleDecimalModelBinderTests
{
    [Theory]
    [InlineData("1234.56", 1234.56)]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("150,5", 150.5)]
    [InlineData("2.500", 2500)]
    public async Task Binds_amounts_typed_with_either_separator(string raw, double expected)
    {
        var context = CreateContext(typeof(decimal), raw);

        await new FlexibleDecimalModelBinder().BindModelAsync(context);

        Assert.True(context.Result.IsModelSet);
        Assert.Equal((decimal)expected, context.Result.Model);
        Assert.Equal(0, context.ModelState.ErrorCount);
    }

    [Fact]
    public async Task Empty_value_binds_null_for_nullable_decimal()
    {
        var context = CreateContext(typeof(decimal?), "");

        await new FlexibleDecimalModelBinder().BindModelAsync(context);

        Assert.True(context.Result.IsModelSet);
        Assert.Null(context.Result.Model);
        Assert.Equal(0, context.ModelState.ErrorCount);
    }

    [Fact]
    public async Task Garbage_value_adds_a_model_error()
    {
        var context = CreateContext(typeof(decimal), "on iki");

        await new FlexibleDecimalModelBinder().BindModelAsync(context);

        Assert.False(context.Result.IsModelSet);
        Assert.Equal(1, context.ModelState.ErrorCount);
    }

    [Fact]
    public async Task Missing_value_leaves_the_result_unset_without_errors()
    {
        var context = CreateContext(typeof(decimal), null);

        await new FlexibleDecimalModelBinder().BindModelAsync(context);

        Assert.False(context.Result.IsModelSet);
        Assert.Equal(0, context.ModelState.ErrorCount);
    }

    [Fact]
    public void Provider_is_registered_ahead_of_the_default_binders()
    {
        var options = new MvcOptions();
        options.ModelBinderProviders.Add(new Microsoft.AspNetCore.Mvc.ModelBinding.Binders.SimpleTypeModelBinderProvider());

        options.UseFlexibleDecimalBinding();

        Assert.IsType<FlexibleDecimalModelBinderProvider>(options.ModelBinderProviders[0]);
    }

    private static DefaultModelBindingContext CreateContext(Type modelType, string? raw)
    {
        var values = new Dictionary<string, StringValues>();
        if (raw is not null)
        {
            values["Amount"] = raw;
        }

        var metadataProvider = new EmptyModelMetadataProvider();
        return new DefaultModelBindingContext
        {
            ModelMetadata = metadataProvider.GetMetadataForType(modelType),
            ModelName = "Amount",
            ModelState = new ModelStateDictionary(),
            ValueProvider = new FormValueProvider(
                BindingSource.Form,
                new FormCollection(values),
                CultureInfo.GetCultureInfo("tr-TR")),
        };
    }
}
