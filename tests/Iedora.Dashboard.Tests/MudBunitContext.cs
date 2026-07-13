using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Iedora.Dashboard.Tests;

/// <summary>
/// bUnit context for components that use MudBlazor: registers the Mud services and runs JSInterop in
/// loose mode (Mud components call into JS for popovers/resize/keyboard, which have no effect in a
/// headless render). Any test that renders a page or component built on MudBlazor derives from this.
/// </summary>
public abstract class MudBunitContext : BunitContext
{
    protected MudBunitContext()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }
}
