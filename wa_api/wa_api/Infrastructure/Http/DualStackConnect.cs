using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace wa_api.Infrastructure.Http;

/// <summary>
/// A <see cref="SocketsHttpHandler.ConnectCallback"/> that tries IPv4 before IPv6 and falls through
/// on failure, with a short per-address timeout.
/// <para>
/// .NET 8's <see cref="SocketsHttpHandler"/> connects to resolved addresses in the order DNS returns
/// them and does NOT implement Happy Eyeballs (RFC 8305). When a host publishes an AAAA record that
/// the network silently blackholes — which is exactly what some ISPs do to Meta — every request hangs
/// until the client's own timeout and then fails, with nothing in the error to suggest the cause.
/// Observed here: <c>graph.facebook.com</c> over IPv6 never completed a connect, while IPv4 connected
/// in 90ms. <c>curl</c> looked healthy throughout because it does implement Happy Eyeballs.
/// </para>
/// <para>
/// This is deliberately a preference, not a restriction: IPv6 is still tried when IPv4 fails or is not
/// published, so a v6-only network still works. The cost of the preference is one extra RTT on a
/// network where v6 would have been better, which is a fair trade against a 15-second hang.
/// </para>
/// </summary>
public static class DualStackConnect
{
    /// <summary>
    /// How long a single address gets before the next is tried. Long enough for an intercontinental
    /// handshake, short enough that exhausting a few dead addresses still beats the client timeout.
    /// </summary>
    private static readonly TimeSpan PerAddressTimeout = TimeSpan.FromSeconds(4);

    public static async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port;

        // Handles an IP literal too — it returns it unchanged rather than hitting a resolver.
        var addresses = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
        if (addresses.Length == 0)
            throw new HttpRequestException($"No DNS records for '{host}'.");

        Exception? lastError = null;

        foreach (var address in PreferIPv4(addresses))
        {
            ct.ThrowIfCancellationRequested();

            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true,     // an API call is a small request; Nagle only adds latency
            };

            try
            {
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
                attempt.CancelAfter(PerAddressTimeout);

                await socket.ConnectAsync(new IPEndPoint(address, port), attempt.Token)
                    .ConfigureAwait(false);

                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // The CALLER gave up (request timeout / shutdown). That is not an address failure and
                // must not be retried against the next one.
                socket.Dispose();
                throw;
            }
            catch (Exception ex)
            {
                // This address is unreachable or timed out on its own budget — try the next.
                socket.Dispose();
                lastError = ex;
            }
        }

        throw new HttpRequestException(
            $"Could not connect to '{host}:{port}' on any resolved address.", lastError);
    }

    /// <summary>
    /// IPv4 first, then everything else, preserving DNS order within each family so round-robin
    /// records still spread load.
    /// </summary>
    public static IEnumerable<IPAddress> PreferIPv4(IReadOnlyList<IPAddress> addresses) =>
        addresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork)
            .Concat(addresses.Where(a => a.AddressFamily != AddressFamily.InterNetwork));
}
