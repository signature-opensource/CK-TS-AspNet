using CK.Setup;
using CK.Testing;
using System.Threading.Tasks;
using NUnit.Framework;

using static CK.Testing.MonitorTestHelper;

namespace CK.TS.AspNet.WebSocketChannel.Tests;

/// <summary>
/// Runs the jest tests of <c>WSConnection</c>.
/// <para>
/// No server is started: the client is exercised against a fake WebSocket installed on the global, so
/// what is tested is the state machine - negotiation, topic routing, reconnection backoff, staleness
/// guards - and nothing depends on a backend being up.
/// </para>
/// </summary>
[TestFixture]
public class WSConnectionTSTests
{
    [Test]
    public async Task CK_AspNet_WebSocketChannel_Async()
    {
        var targetProjectPath = TestHelper.GetTypeScriptInlineTargetProjectPath();

        var engineConfig = TestHelper.CreateDefaultEngineConfiguration();
        engineConfig.FirstBinPath.Assemblies.Add( "CK.TS.AspNet.WebSocketChannel" );
        engineConfig.FirstBinPath.EnsureTypeScriptConfigurationAspect( targetProjectPath );
        await engineConfig.RunSuccessfullyAsync();

        await using var runner = TestHelper.CreateTypeScriptRunner( targetProjectPath );
        await TestHelper.SuspendAsync( resume => resume );
        runner.Run();
    }
}
