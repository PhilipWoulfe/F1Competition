using System.Linq.Expressions;
using F1.Web.Components;
using F1.Web.Models;
using Microsoft.AspNetCore.Components;

namespace F1.Web.Tests.Components;

public class BetStrategySelectorTests : BunitContext
{
    [Fact]
    public void BetStrategySelector_ShouldRenderAllOptions_AndReflectCurrentValue()
    {
        var currentValue = BetType.PreQualy;
        Expression<Func<BetType>> valueExpression = () => currentValue;

        var cut = Render<BetStrategySelector>(parameters => parameters
            .Add(p => p.Value, currentValue)
            .Add(p => p.ValueExpression, valueExpression)
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<BetType>(new object(), _ => { }))
            .Add(p => p.Options, CreateOptions())
            .Add(p => p.IsReadOnly, false));

        Assert.Contains("Regular", cut.Markup);
        Assert.Contains("Pre-Qualy", cut.Markup);
        Assert.Contains("All-or-Nothing", cut.Markup);
        Assert.True(cut.Find("#strategy-prequaly").HasAttribute("checked"));
    }

    [Fact]
    public void BetStrategySelector_ShouldDisableAllOptions_WhenReadOnly()
    {
        var currentValue = BetType.Regular;
        Expression<Func<BetType>> valueExpression = () => currentValue;

        var cut = Render<BetStrategySelector>(parameters => parameters
            .Add(p => p.Value, currentValue)
            .Add(p => p.ValueExpression, valueExpression)
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<BetType>(new object(), _ => { }))
            .Add(p => p.Options, CreateOptions())
            .Add(p => p.IsReadOnly, true));

        Assert.True(cut.FindAll("input[type='radio']").All(element => element.HasAttribute("disabled")));
    }

    [Fact]
    public void BetStrategySelector_ShouldUpdateCheckedOption_WhenValueChanges()
    {
        var initialValue = BetType.Regular;
        Expression<Func<BetType>> initialExpression = () => initialValue;
        var initial = Render<BetStrategySelector>(parameters => parameters
            .Add(p => p.Value, initialValue)
            .Add(p => p.ValueExpression, initialExpression)
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<BetType>(new object(), _ => { }))
            .Add(p => p.Options, CreateOptions())
            .Add(p => p.IsReadOnly, false));

        Assert.True(initial.Find("#strategy-regular").HasAttribute("checked"));

        var updatedValue = BetType.AllOrNothing;
        Expression<Func<BetType>> updatedExpression = () => updatedValue;
        var updated = Render<BetStrategySelector>(parameters => parameters
            .Add(p => p.Value, updatedValue)
            .Add(p => p.ValueExpression, updatedExpression)
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<BetType>(new object(), _ => { }))
            .Add(p => p.Options, CreateOptions())
            .Add(p => p.IsReadOnly, false));

        Assert.True(updated.Find("#strategy-allornothing").HasAttribute("checked"));
    }

    [Fact]
    public void BetStrategySelector_ShouldDisableUnavailableOption_WithoutDisablingAvailableOnes()
    {
        var currentValue = BetType.Regular;
        Expression<Func<BetType>> valueExpression = () => currentValue;

        var cut = Render<BetStrategySelector>(parameters => parameters
            .Add(p => p.Value, currentValue)
            .Add(p => p.ValueExpression, valueExpression)
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<BetType>(new object(), _ => { }))
            .Add(p => p.Options, CreateOptions(preQualyAvailable: false))
            .Add(p => p.IsReadOnly, false));

        Assert.False(cut.Find("#strategy-regular").HasAttribute("disabled"));
        Assert.True(cut.Find("#strategy-prequaly").HasAttribute("disabled"));
    }

    private static BetOption[] CreateOptions(bool preQualyAvailable = true)
    {
        return
        [
            new BetOption { BetType = BetType.Regular, Label = "Regular", IsAvailable = true },
            new BetOption { BetType = BetType.PreQualy, Label = "Pre-Qualy", IsAvailable = preQualyAvailable },
            new BetOption { BetType = BetType.AllOrNothing, Label = "All-or-Nothing", IsAvailable = true }
        ];
    }
}
