using CK.AspNet;
using CK.Core;
using Microsoft.Extensions.DependencyInjection;

namespace CK.TypeScript;

/// <summary>
/// Registers what <see cref="ClientsApplicationExtensions.MapClients"/> requires (the HTTP forwarder that reaches the
/// dev servers): the hosts don't call <c>ReverseProxyServiceCollectionExtensions.AddHttpForwarder()</c>.
/// <para>
/// It also contributes <see cref="ClientsApplicationExtensions.EnableClients"/> to the web applications of the host
/// (<see cref="WebApplicationContributions"/>): a host that applies the contributions (the "HttpEndpoint" feature of
/// CK.AppIdentity.HttpEndpoint) serves the client applications without calling anything.
/// </para>
/// </summary>
public sealed class ClientsHosting : IRealObject
{
    void ConfigureServices( StObjContextRoot.ServiceRegister services )
    {
        services.Services.AddHttpForwarder();
        WebApplicationContributions.FindOrAdd( services.Services ).Add( web => web.EnableClients() );
    }
}
