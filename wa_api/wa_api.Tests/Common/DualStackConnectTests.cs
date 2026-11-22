using System.Net;
using wa_api.Infrastructure.Http;
using Xunit;

namespace wa_api.Tests.Common;

/// <summary>
/// Pins the address preference that keeps outbound calls off a blackholed IPv6 path.
/// <para>
/// Measured against graph.facebook.com from a network where Meta's AAAA route is dropped: IPv6 never
/// completed a connect, IPv4 connected in 90ms. .NET 8 has no Happy Eyeballs, so it sat on the dead
/// address until the 15-second client timeout — which surfaced as WhatsApp sends failing and the WABA
/// connection being marked Disconnected, with nothing pointing at the network.
/// </para>
/// </summary>
public class DualStackConnectTests
{
    private static IPAddress V4(string s) => IPAddress.Parse(s);
    private static IPAddress V6(string s) => IPAddress.Parse(s);

    [Fact]
    public void IPv4_IsTriedBeforeIPv6()
    {
        IPAddress[] resolved = [V6("2a03:2880:f34b:8d:face:b00c:0:2"), V4("57.144.150.141")];

        var ordered = DualStackConnect.PreferIPv4(resolved).ToList();

        Assert.Equal(V4("57.144.150.141"), ordered[0]);
        Assert.Equal(V6("2a03:2880:f34b:8d:face:b00c:0:2"), ordered[1]);
    }

    [Fact]
    public void IPv6_IsStillOfferedSoAV6OnlyNetworkWorks()
    {
        // A preference, not a restriction — dropping v6 entirely would break v6-only hosting.
        IPAddress[] resolved = [V6("2607:6bc0::10")];

        Assert.Equal(resolved, DualStackConnect.PreferIPv4(resolved));
    }

    [Fact]
    public void DnsOrderIsPreservedWithinEachFamily()
    {
        // Round-robin DNS spreads load across A records; reordering them would concentrate it.
        IPAddress[] resolved =
        [
            V4("1.1.1.1"), V6("2607:6bc0::10"), V4("2.2.2.2"), V6("2607:6bc0::11"), V4("3.3.3.3"),
        ];

        var ordered = DualStackConnect.PreferIPv4(resolved).ToList();

        Assert.Equal([V4("1.1.1.1"), V4("2.2.2.2"), V4("3.3.3.3")], ordered.Take(3));
        Assert.Equal([V6("2607:6bc0::10"), V6("2607:6bc0::11")], ordered.Skip(3));
    }

    [Fact]
    public void EveryResolvedAddressIsKept()
    {
        IPAddress[] resolved = [V6("2607:6bc0::10"), V4("1.1.1.1"), V6("2607:6bc0::11")];

        Assert.Equal(resolved.Length, DualStackConnect.PreferIPv4(resolved).Count());
    }

    [Fact]
    public void AnEmptyListStaysEmptyRatherThanThrowing()
    {
        Assert.Empty(DualStackConnect.PreferIPv4([]));
    }
}
