using Ships.Interface.Model.Parts.State;
using UnityEngine;
using Xunit;

namespace StellarPerformance.Tests;

public sealed class VisualTradeoffTests
{
    [Fact]
    public void ExpandedBatchCoverageIncludesOnlyInspectedFixedBodiesAndGenericScripts()
    {
        Assert.Contains(typeof(Ships.Parts.EthanolDistiller.EthanolDistillerVisuals), ShipPartBatching.FixedTypes);
        Assert.Contains(typeof(Ships.Parts.Electrolyzer.ElectrolyzerVisuals), ShipPartBatching.FixedTypes);
        Assert.Contains(typeof(Ships.Parts.Common.DefaultShipPartVisuals), ShipPartBatching.FixedTypes);
        Assert.DoesNotContain(typeof(Ships.Parts.Door.DoorVisuals), ShipPartBatching.FixedTypes);
        Assert.DoesNotContain(typeof(Ships.Parts.WoodPowerGenerator.PowerGeneratorVisuals), ShipPartBatching.FixedTypes);
        Assert.DoesNotContain(typeof(Ships.Parts.FluidPump.FluidPumpVisuals), ShipPartBatching.FixedTypes);
        Assert.True(ShipPartBatching.StaticGenericComponent(typeof(Ships.Parts.Common.DefaultShipPartVisuals)));
        Assert.False(ShipPartBatching.StaticGenericComponent(typeof(Ships.Parts.WoodPowerGenerator.PowerGeneratorVisuals)));
        Assert.False(ShipPartBatching.StaticGenericComponent(typeof(MonoBehaviour)));
    }

    [Fact]
    public void NumericReadoutsCoalesceAtTenHzAndFlushLatestValue()
    {
        var state = new WorldUiVisibility.PendingDisplay { Displayed = new SignalDisplayState(), NextRefresh = 0.1f };
        Assert.False(state.Observe(new SignalDisplayState { SignalValue = 1 }, true, 0.02f, 0.1f, false));
        Assert.False(state.Observe(new SignalDisplayState { SignalValue = 2 }, true, 0.08f, 0.1f, false));
        Assert.True(state.Pending);
        Assert.Equal(2, ((SignalDisplayState)state.Latest!).SignalValue);
        Assert.True(state.Observe(new SignalDisplayState { SignalValue = 3 }, true, 0.1f, 0.1f, false));
        Assert.False(state.Pending);
        Assert.Equal(3, ((SignalDisplayState)state.Displayed!).SignalValue);
    }

    [Fact]
    public void ReentryFocusedFieldsAndControlChangesBypassNumericThrottle()
    {
        var state = new WorldUiVisibility.PendingDisplay { Displayed = new SignalDisplayState(), NextRefresh = 10 };
        Assert.False(state.Observe(new SignalDisplayState { SignalValue = 1 }, false, 0, 0.1f, false));
        Assert.True(state.Observe(new SignalDisplayState { SignalValue = 2 }, true, 0.01f, 0.1f, false));
        Assert.True(state.Observe(new SignalDisplayState { SignalValue = 3 }, true, 0.02f, 0.1f, true));
        Assert.True(state.Observe(new MathGateState(), true, 0.03f, 0.1f, false));
        Assert.False(state.Observe(new MathGateState(), true, 0.04f, 0.1f, false));
    }

    [Fact]
    public void RevertingPendingNumericValueCancelsRefresh()
    {
        var original = new GasMeterState { Density = 1 };
        var state = new WorldUiVisibility.PendingDisplay { Displayed = original, NextRefresh = 10 };
        Assert.False(state.Observe(new GasMeterState { Density = 2 }, true, 0, 0.1f, false));
        Assert.False(state.Observe(original, true, 0.01f, 0.1f, false));
        Assert.False(state.Pending);
    }

    [Fact]
    public void ProjectedBoundsKeepNearPlaneIntersectionsAndMagnifiedViews()
    {
        Assert.True(float.IsPositiveInfinity(RendererDetail.ProjectedDiameter(1, 1, 1000, false)));
        Assert.True(float.IsPositiveInfinity(RendererDetail.ProjectedDiameter(float.NaN, 100, 1000, false)));
        Assert.True(RendererDetail.ProjectedDiameter(1, 100, 1000, false) > 20);
        Assert.Equal(20, RendererDetail.ProjectedDiameter(1, 100, 10, true));
        Assert.False(RendererDetail.HideSmall(false, 3, 3));
        Assert.True(RendererDetail.HideSmall(false, 2.9f, 3));
        Assert.True(RendererDetail.HideSmall(true, 4, 3));
        Assert.False(RendererDetail.HideSmall(true, 4.5f, 3));
        Assert.False(RendererDetail.HideSmall(true, 0, 0));
    }

    [Fact]
    public void ShadowCutoffHasHysteresisAndSupportsNativeOverride()
    {
        Assert.False(RendererDetail.SuppressShadows(false, 40, 40));
        Assert.True(RendererDetail.SuppressShadows(false, 41, 40));
        Assert.True(RendererDetail.SuppressShadows(true, 37, 40));
        Assert.False(RendererDetail.SuppressShadows(true, 36, 40));
        Assert.False(RendererDetail.SuppressShadows(true, 1000, 0));
        Assert.Equal(10, PerformancePlugin.FiniteRange(float.NaN, 10, 0, 60));
        Assert.Equal(60, PerformancePlugin.FiniteRange(1000, 10, 0, 60));
    }

    [Fact]
    public void RelevantNearbyAndZoomedCamerasPreserveDetailWhileOtherLayersDoNot()
    {
        var bounds = new Bounds(new Vector3(0, 0, 100), new Vector3(0.1f, 0.1f, 0.1f));
        var normal = new RendererDetail.View(Vector3.zero, Vector3.forward, 1, 1000, 0.1f, 9000, false);
        var zoom = new RendererDetail.View(Vector3.zero, Vector3.forward, 1, 10000, 0.1f, 9000, false);
        var nearby = new RendererDetail.View(new Vector3(0, 0, 90), Vector3.forward, 1, 1000, 0.1f, 9000, false);
        var otherLayer = new RendererDetail.View(nearby.Position, nearby.Forward, 2, 10000, 0.1f, 9000, false);
        RendererDetail.ChooseDetail(bounds, 1, new[] { normal }, 1, false, false, out var hidden, out var distant);
        Assert.True(hidden); Assert.True(distant);
        RendererDetail.ChooseDetail(bounds, 1, new[] { normal, zoom }, 2, true, true, out hidden, out distant);
        Assert.False(hidden); Assert.True(distant);
        RendererDetail.ChooseDetail(bounds, 1, new[] { normal, nearby }, 2, true, true, out hidden, out distant);
        Assert.False(hidden); Assert.False(distant);
        RendererDetail.ChooseDetail(bounds, 1, new[] { normal, otherLayer }, 2, false, false, out hidden, out distant);
        Assert.True(hidden); Assert.True(distant);
        RendererDetail.ChooseDetail(bounds, 1, new[] { otherLayer }, 1, true, true, out hidden, out distant);
        Assert.False(hidden); Assert.False(distant);
    }
}
