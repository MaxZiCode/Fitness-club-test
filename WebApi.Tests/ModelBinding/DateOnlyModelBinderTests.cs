using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Primitives;
using WebApi.ModelBinding;

namespace WebApi.Tests.ModelBinding;

[TestClass]
public sealed class DateOnlyModelBinderTests
{
    private const string ModelName = "date";

    private ModelStateDictionary _modelState = null!;
    private DateOnlyModelBinder _binder = null!;
    private EmptyModelMetadataProvider _metadataProvider = null!;

    [TestInitialize]
    public void Setup()
    {
        _modelState = new ModelStateDictionary();
        _binder = new DateOnlyModelBinder();
        _metadataProvider = new EmptyModelMetadataProvider();
    }

    [TestMethod]
    public async Task BindModelAsync_ValueNotFound_DoesNotSetResultOrModelState()
    {
        var context = CreateBindingContext(ValueProviderResult.None);

        await _binder.BindModelAsync(context);

        Assert.IsFalse(context.Result.IsModelSet);
        Assert.IsFalse(_modelState.ContainsKey(ModelName));
    }

    [TestMethod]
    public async Task BindModelAsync_EmptyValue_DoesNotSetResult()
    {
        var context = CreateBindingContext(new ValueProviderResult(new StringValues("")));

        await _binder.BindModelAsync(context);

        Assert.IsFalse(context.Result.IsModelSet);
        Assert.IsTrue(_modelState.ContainsKey(ModelName));
    }

    [TestMethod]
    public async Task BindModelAsync_WhitespaceValue_DoesNotSetResult()
    {
        var context = CreateBindingContext(new ValueProviderResult(new StringValues("   ")));

        await _binder.BindModelAsync(context);

        Assert.IsFalse(context.Result.IsModelSet);
        Assert.IsTrue(_modelState.ContainsKey(ModelName));
    }

    [TestMethod]
    public async Task BindModelAsync_ValidDate_ReturnsDateOnly()
    {
        var input = "2026-09-22";
        var expected = new DateOnly(2026, 9, 22);
        var context = CreateBindingContext(new ValueProviderResult(new StringValues(input)));

        await _binder.BindModelAsync(context);

        Assert.IsTrue(context.Result.IsModelSet);
        Assert.AreEqual(expected, context.Result.Model);
        Assert.IsInstanceOfType<DateOnly>(context.Result.Model);
        Assert.IsFalse(_modelState.ContainsKey(ModelName) && _modelState[ModelName]!.Errors.Count > 0);
    }

    [TestMethod]
    public async Task BindModelAsync_DateWithTimeComponent_AddsModelError()
    {
        var input = "2026-09-22T12:30:00";
        var context = CreateBindingContext(new ValueProviderResult(new StringValues(input)));

        await _binder.BindModelAsync(context);

        Assert.IsFalse(context.Result.IsModelSet);
        Assert.IsTrue(_modelState.TryGetValue(ModelName, out var entry));
        Assert.IsNotNull(entry);
    }

    [TestMethod]
    public async Task BindModelAsync_DateWithOffset_AddsModelError()
    {
        var input = "2026-09-22+03:00";
        var context = CreateBindingContext(new ValueProviderResult(new StringValues(input)));

        await _binder.BindModelAsync(context);

        Assert.IsFalse(context.Result.IsModelSet);
        Assert.IsTrue(_modelState.TryGetValue(ModelName, out var entry));
        Assert.IsNotNull(entry);
        Assert.AreEqual(
            $"The value '{input}' is not a valid date. Expected format is yyyy-MM-dd without a time component.",
            entry.Errors.Single().ErrorMessage);
    }

    [TestMethod]
    public async Task BindModelAsync_InvalidDateFormat_AddsModelError()
    {
        var input = "22-09-2026";
        var context = CreateBindingContext(new ValueProviderResult(new StringValues(input)));

        await _binder.BindModelAsync(context);

        Assert.IsFalse(context.Result.IsModelSet);
        Assert.IsTrue(_modelState.TryGetValue(ModelName, out var entry));
        Assert.IsNotNull(entry);
        Assert.AreEqual(
            $"The value '{input}' is not a valid date. Expected format is yyyy-MM-dd without a time component.",
            entry.Errors.Single().ErrorMessage);
    }

    [TestMethod]
    public async Task BindModelAsync_InvalidCalendarDate_AddsModelError()
    {
        var input = "2026-02-30";
        var context = CreateBindingContext(new ValueProviderResult(new StringValues(input)));

        await _binder.BindModelAsync(context);

        Assert.IsFalse(context.Result.IsModelSet);
        Assert.IsTrue(_modelState.TryGetValue(ModelName, out var entry));
        Assert.IsNotNull(entry);
        Assert.AreEqual(
            $"The value '{input}' is not a valid date. Expected format is yyyy-MM-dd without a time component.",
            entry.Errors.Single().ErrorMessage);
    }

    [TestMethod]
    public async Task BindModelAsync_ValidLeapDay_ReturnsDateOnly()
    {
        var input = "2024-02-29";
        var expected = new DateOnly(2024, 2, 29);
        var context = CreateBindingContext(new ValueProviderResult(new StringValues(input)));

        await _binder.BindModelAsync(context);

        Assert.IsTrue(context.Result.IsModelSet);
        Assert.AreEqual(expected, context.Result.Model);
    }

    [TestMethod]
    public async Task BindModelAsync_SetsAttemptedValueInModelState()
    {
        var input = "2026-09-22";
        var context = CreateBindingContext(new ValueProviderResult(new StringValues(input)));

        await _binder.BindModelAsync(context);

        Assert.IsTrue(_modelState.TryGetValue(ModelName, out var entry));
        Assert.IsNotNull(entry);
        Assert.AreEqual(input, entry.AttemptedValue);
    }

    private DefaultModelBindingContext CreateBindingContext(ValueProviderResult valueProviderResult)
    {
        var valueProvider = new MockValueProvider(ModelName, valueProviderResult);

        return new DefaultModelBindingContext
        {
            ModelName = ModelName,
            FieldName = ModelName,
            ModelMetadata = _metadataProvider.GetMetadataForType(typeof(DateOnly)),
            ModelState = _modelState,
            ValueProvider = valueProvider,
            ActionContext = new ActionContext()
        };
    }

    private sealed class MockValueProvider(string modelName, ValueProviderResult valueResult) : IValueProvider
    {
        public bool ContainsPrefix(string prefix) => prefix == modelName;

        public ValueProviderResult GetValue(string key) =>
            key == modelName ? valueResult : ValueProviderResult.None;
    }
}
