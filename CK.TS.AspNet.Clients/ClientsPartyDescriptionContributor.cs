using CK.AppIdentity;
using CK.Core;
using System;

namespace CK.TypeScript;

/// <summary>
/// Contributes the client applications of the <see cref="ClientDirectory"/> to the description of the party (its
/// <see cref="IParty.Description"/>, see the "PartyDescription" feature of CK.AppIdentity): their name and request path.
/// The remotes of the party know them (they are announced with its public URL): the links that a remote builds for this
/// party (the mails of an Authentication Center) target them.
/// </summary>
public sealed class ClientsPartyDescriptionContributor : IPartyDescriptionContributor
{
    readonly ClientDirectory _directory;

    /// <summary>
    /// Initializes a new <see cref="ClientsPartyDescriptionContributor"/>.
    /// </summary>
    /// <param name="directory">The client applications of the host.</param>
    public ClientsPartyDescriptionContributor( ClientDirectory directory )
    {
        _directory = directory;
    }

    /// <inheritdoc />
    public void Activate( IActivityMonitor monitor, Action onChanged )
    {
        // The client applications are known by the generated code: they never change.
    }

    /// <inheritdoc />
    public void Contribute( PartyDescription.Builder builder )
    {
        foreach( var c in _directory.Clients )
        {
            builder.AddClientApplication( c.Name, c.RequestPath );
        }
    }
}
