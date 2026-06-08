// SSRF guard for OUTBOUND calls to tenant-supplied hosts (GitHub Enterprise URLs, Azure DevOps hosts,
// GitHub Projects GraphQL endpoints). A tenant could point a "workspace host" at an internal address
// (cloud metadata 169.254.169.254, localhost, RFC1918) and make the server fetch it on their behalf.
//
// The defense is at CONNECT time, not by string-matching the host: CreateHardenedHandler resolves the
// target host and refuses to open a socket to any private/loopback/link-local/ULA/multicast address. This
// covers all three vectors a host-string check misses — IP literals, hostnames that RESOLVE to a private
// IP, and HTTP redirects to a private IP (the handler runs again for the redirected request). Connect-time
// pinning to the vetted address also closes the DNS-rebinding TOCTOU window.

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace AgentOs.SharedKernel.Security;

/// <summary>Builds an <see cref="HttpMessageHandler"/> that blocks connections to private/internal IPs, and
/// exposes the pure address predicate for testing.</summary>
public static class SsrfGuard
{
    /// <summary>A <see cref="SocketsHttpHandler"/> whose connect step refuses private/loopback/link-local
    /// targets. Returns a fresh handler each call (give one per <see cref="HttpClient"/> / factory).</summary>
    public static SocketsHttpHandler CreateHardenedHandler() => new()
    {
        ConnectCallback = async (context, cancellationToken) =>
        {
            var host = context.DnsEndPoint.Host;
            var port = context.DnsEndPoint.Port;

            var candidates = IPAddress.TryParse(host, out var literal)
                ? [literal]
                : await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);

            var allowed = candidates.Where(a => !IsBlockedAddress(a)).ToArray();
            if (allowed.Length == 0)
            {
                throw new HttpRequestException(
                    $"Refusing to connect to '{host}': it resolves only to blocked (private/loopback/link-local) addresses.");
            }

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(allowed, port, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
            return new NetworkStream(socket, ownsSocket: true);
        },
    };

    /// <summary><c>true</c> if connecting to <paramref name="address"/> should be refused — loopback,
    /// "this network", RFC1918 private, CGNAT, link-local (incl. cloud metadata 169.254.169.254), IPv6
    /// ULA/link-local/site-local, multicast, or reserved ranges.</summary>
    public static bool IsBlockedAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] switch
            {
                0 => true,                                  // 0.0.0.0/8 "this network"
                10 => true,                                 // 10.0.0.0/8 private
                100 when b[1] is >= 64 and <= 127 => true,  // 100.64.0.0/10 CGNAT
                127 => true,                                // 127.0.0.0/8 loopback
                169 when b[1] == 254 => true,               // 169.254.0.0/16 link-local (incl. metadata)
                172 when b[1] is >= 16 and <= 31 => true,   // 172.16.0.0/12 private
                192 when b[1] == 168 => true,               // 192.168.0.0/16 private
                >= 224 => true,                             // 224.0.0.0/4 multicast + 240.0.0.0/4 reserved
                _ => false,
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal
                || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.IPv6Loopback))
            {
                return true;
            }
            // fc00::/7 unique-local addresses.
            return (address.GetAddressBytes()[0] & 0xFE) == 0xFC;
        }

        return false;
    }
}
