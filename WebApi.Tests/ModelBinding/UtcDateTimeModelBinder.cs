using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Primitives;
using Moq;
using WebApi.ModelBinding;

namespace WebApi.Tests.ModelBinding;

[TestClass]
public sealed class UtcDateTimeModelBinderTests
{
    private const string ModelName = "date";

    private Mock<IValueProvider> _valueProviderMock = null!;
    private ModelStateDictionary _modelState = null!;
    private UtcDateTimeModelBinder _binder = null!;

    [TestInitialize]
    public void Setup()
    {
        _valueProviderMock = new Mock<IValueProvider>();
        _modelState = new ModelStateDictionary();
        _binder = new UtcDateTimeModelBinder();
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
    public async Task BindModelAsync_UtcValueWithoutOffset_ReturnsUtcDateTime()
    {
        var input = "2026-10-27T08:30:45";
        var expected = new DateTime(2026, 10, 27, 8, 30, 45, DateTimeKind.Utc);
        var context = CreateBindingContext(new ValueProviderResult(new StringValues(input)));

        await _binder.BindModelAsync(context);

        Assert.IsTrue(context.Result.IsModelSet);
        Assert.AreEqual(expected, context.Result.Model);
        Assert.AreEqual(DateTimeKind.Utc, ((DateTime)context.Result.Model!).Kind);
        Assert.IsTrue(_modelState.ContainsKey(ModelName));
    }

    [TestMethod]
    public async Task BindModelAsync_ValueWithPositiveOffset_ConvertsToUtc()
    {
        var input = "2026-10-27T11:30:45+03:00";
        var expected = new DateTime(2026, 10, 27, 8, 30, 45, DateTimeKind.Utc);
        var context = CreateBindingContext(new ValueProviderResult(new StringValues(input)));

        await _binder.BindModelAsync(context);

        Assert.IsTrue(context.Result.IsModelSet);
        Assert.AreEqual(expected, context.Result.Model);
        Assert.AreEqual(DateTimeKind.Utc, ((DateTime)context.Result.Model!).Kind);
    }

    [TestMethod]
    public async Task BindModelAsync_ValueWithNegativeOffset_ConvertsToUtc()
    {
        var input = "2026-10-27T04:30:45-04:00";
        var expected = new DateTime(2026, 10, 27, 8, 30, 45, DateTimeKind.Utc);
        var context = CreateBindingContext(new ValueProviderResult(new StringValues(input)));

        await _binder.BindModelAsync(context);

        Assert.IsTrue(context.Result.IsModelSet);
        Assert.AreEqual(expected, context.Result.Model);
        Assert.AreEqual(DateTimeKind.Utc, ((DateTime)context.Result.Model!).Kind);
    }

    [TestMethod]
    public async Task BindModelAsync_ValueWithZuluSuffix_ParsesAsUtc()
    {
        var input = "2026-10-27T08:30:45Z";
        var expected = new DateTime(2026, 10, 27, 8, 30, 45, DateTimeKind.Utc);
        var context = CreateBindingContext(new ValueProviderResult(new StringValues(input)));

        await _binder.BindModelAsync(context);

        Assert.IsTrue(context.Result.IsModelSet);
        Assert.AreEqual(expected, context.Result.Model);
        Assert.AreEqual(DateTimeKind.Utc, ((DateTime)context.Result.Model!).Kind);
    }

    [TestMethod]
    public async Task BindModelAsync_InvalidValue_AddsModelError()
    {
        var context = CreateBindingContext(new ValueProviderResult(new StringValues("not-a-date")));

        await _binder.BindModelAsync(context);

        Assert.IsFalse(context.Result.IsModelSet);
        Assert.IsTrue(_modelState.TryGetValue(ModelName, out var entry));
        Assert.IsNotNull(entry);
    }

    [TestMethod]
    public async Task BindModelAsync_SetsAttemptedValueInModelState()
    {
        var input = "2026-10-27T08:30:45";
        var context = CreateBindingContext(new ValueProviderResult(new StringValues(input)));

        await _binder.BindModelAsync(context);

        Assert.IsTrue(_modelState.TryGetValue(ModelName, out var entry));
        Assert.IsNotNull(entry);
        Assert.AreEqual(input, entry.AttemptedValue);
    }

    private ModelBindingContext CreateBindingContext(ValueProviderResult valueProviderResult)
    {
        _valueProviderMock
            .Setup(provider => provider.GetValue(ModelName))
            .Returns(valueProviderResult);

        var metadataProvider = new EmptyModelMetadataProvider();
        var metadata = metadataProvider.GetMetadataForType(typeof(DateTime));

        var context = new DefaultModelBindingContext
        {
            ModelName = ModelName,
            ModelMetadata = metadata,
            ModelState = _modelState,
            ValueProvider = _valueProviderMock.Object,
            FieldName = ModelName,
            ActionContext = new Microsoft.AspNetCore.Mvc.ActionContext()
        };

        return context;
    }
}
