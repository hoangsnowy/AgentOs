// SSRF guard: the connect-time predicate must block private/loopback/link-local/ULA/multicast targets
// (incl. the cloud-metadata 169.254.169.254) while allowing public addresses.

using System.Net;
using AgentOs.SharedKernel.Security;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Security;

public sealed class SsrfGuardTests
{
    [Theory]
    [InlineData("127.0.0.1")]      // loopback
    [InlineData("10.0.0.5")]       // RFC1918
    [InlineData("172.16.9.9")]     // RFC1918
    [InlineData("172.31.255.255")] // RFC1918 upper edge
    [InlineData("192.168.1.1")]    // RFC1918
    [InlineData("169.254.169.254")]// link-local / cloud metadata
    [InlineData("100.64.0.1")]     // CGNAT
    [InlineData("0.0.0.0")]        // "this network"
    [InlineData("224.0.0.1")]      // multicast
    [InlineData("::1")]            // IPv6 loopback
    [InlineData("fe80::1")]        // IPv6 link-local
    [InlineData("fc00::1")]        // IPv6 ULA
    [InlineData("::ffff:127.0.0.1")] // IPv4-mapped loopback
    public void IsBlockedAddress_PrivateOrInternal_True(string ip)
        => SsrfGuard.IsBlockedAddress(IPAddress.Parse(ip)).ShouldBeTrue();

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("140.82.112.3")]   // github.com range
    [InlineData("2606:4700:4700::1111")] // public IPv6 (Cloudflare)
    public void IsBlockedAddress_PublicAddress_False(string ip)
        => SsrfGuard.IsBlockedAddress(IPAddress.Parse(ip)).ShouldBeFalse();

    [Fact]
    public void CreateHardenedHandler_ReturnsHandlerWithConnectCallback()
    {
        using var handler = SsrfGuard.CreateHardenedHandler();
        handler.ConnectCallback.ShouldNotBeNull();
    }
}
