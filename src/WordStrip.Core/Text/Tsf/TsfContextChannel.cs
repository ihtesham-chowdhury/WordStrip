using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace WordStrip.Core.Text.Tsf;

/// <summary>
/// Receives context from text services running inside other applications and feeds it to a
/// <see cref="TsfTextContextProvider"/>.
///
/// <para><b>A named pipe rather than anything cleverer.</b> The senders are DLLs loaded into Chrome, Word and
/// Explorer; they need a channel that exists before they do, survives them coming and going, and costs
/// almost nothing per message. Shared memory would be faster and would need a synchronisation design nobody
/// wants to debug across a dozen host processes; window messages would put the work on the UI thread, which
/// is the one place the phase brief says not to put it.</para>
///
/// <para><b>Several hosts connect at once.</b> Every application with the service loaded holds a connection,
/// but only the focused one has a live TSF context, so only one of them is sending. The channel therefore
/// accepts many clients and simply applies whatever arrives — no arbitration, because Windows has already
/// done it by deciding who has focus.</para>
/// </summary>
public sealed class TsfContextChannel : IDisposable
{
    /// <summary>
    /// One pipe per logon session, not one per machine. Two users signed in at once via fast user switching
    /// each run their own WordStrip, and a single fixed name would mean the second one fails to start its
    /// channel with no obvious reason why.
    /// </summary>
    public static string PipeNameForCurrentSession() =>
        $"WordStrip.TextContext.{System.Diagnostics.Process.GetCurrentProcess().SessionId}";

    /// <summary>
    /// Enough for every application the user has open that accepts text, with headroom. A cap exists at all
    /// so that a misbehaving service reconnecting in a loop cannot exhaust handles in the tray process.
    /// </summary>
    private const int MaxConcurrentClients = 32;

    private readonly TsfTextContextProvider _provider;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Action<Exception>? _onError;

    private int _connectedClients;

    public TsfContextChannel(
        TsfTextContextProvider provider,
        string? pipeName = null,
        Action<Exception>? onError = null)
    {
        _provider = provider;
        _pipeName = pipeName ?? PipeNameForCurrentSession();
        _onError = onError;
    }

    public string PipeName => _pipeName;

    /// <summary>How many text services are currently connected. Diagnostics, and the settings window may show it.</summary>
    public int ConnectedClients => Volatile.Read(ref _connectedClients);

    /// <summary>
    /// Starts accepting connections on a background thread. Returns immediately; a channel that cannot start
    /// is reported through <c>onError</c> and leaves the application working on the keyboard hook alone,
    /// because "the TSF path is unavailable" is a state the composite already handles.
    /// </summary>
    public void Start()
    {
        _ = Task.Run(AcceptLoopAsync);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;

            try
            {
                if (ConnectedClients >= MaxConcurrentClients)
                {
                    await Task.Delay(250, _shutdown.Token).ConfigureAwait(false);
                    continue;
                }

                server = CreateServer();
                await server.WaitForConnectionAsync(_shutdown.Token).ConfigureAwait(false);

                var connected = server;
                server = null;  // ownership passes to the reader

                _ = Task.Run(() => ServeClientAsync(connected));
            }
            catch (OperationCanceledException)
            {
                server?.Dispose();
                return;
            }
            catch (Exception ex)
            {
                server?.Dispose();
                _onError?.Invoke(ex);

                // Back off rather than spin. A pipe that cannot be created will usually fail the same way
                // every time, and a tight retry loop would burn a core for the life of the process.
                try { await Task.Delay(1000, _shutdown.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    /// <summary>
    /// Creates the pipe with an ACL naming only the current user.
    ///
    /// <para>Worth the extra lines: the default access for a named pipe would let any local account connect
    /// and send messages, and every message this channel accepts becomes the text WordStrip believes is
    /// around the user's caret. That is not a route to anything dramatic — the worst outcome is wrong
    /// suggestions — but an input channel that anyone on the machine can write to is not something to leave
    /// open because closing it was inconvenient.</para>
    /// </summary>
    private NamedPipeServerStream CreateServer()
    {
        var security = new PipeSecurity();

        var user = WindowsIdentity.GetCurrent().User;
        if (user is not null)
        {
            security.AddAccessRule(new PipeAccessRule(
                user, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
        }

        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            _pipeName,
            PipeDirection.In,
            MaxConcurrentClients,
            PipeTransmissionMode.Message,
            PipeOptions.Asynchronous,
            inBufferSize: TsfContextMessage.MaxBytes,
            outBufferSize: 0,
            pipeSecurity: security);
    }

    private async Task ServeClientAsync(NamedPipeServerStream client)
    {
        Interlocked.Increment(ref _connectedClients);
        _provider.SetConnected(true);

        var buffer = new byte[TsfContextMessage.MaxBytes];

        try
        {
            while (!_shutdown.IsCancellationRequested && client.IsConnected)
            {
                var read = await client.ReadAsync(buffer.AsMemory(), _shutdown.Token).ConfigureAwait(false);
                if (read <= 0) break;  // clean disconnect

                // Message-mode pipes deliver a whole message per read, so a short read is a truncated or
                // malformed message rather than a fragment to accumulate. Dropping it is right: the sender
                // will send another on the next keystroke, and half a context is worse than none.
                var message = TsfContextMessage.TryParse(buffer.AsSpan(0, read));
                if (message is not null) _provider.Apply(message.Value);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
            // The host process went away mid-read. Ordinary: applications close.
        }
        catch (Exception ex)
        {
            _onError?.Invoke(ex);
        }
        finally
        {
            client.Dispose();

            if (Interlocked.Decrement(ref _connectedClients) == 0)
            {
                _provider.SetConnected(false);
            }
        }
    }

    public void Dispose()
    {
        try { _shutdown.Cancel(); } catch (ObjectDisposedException) { }
        _shutdown.Dispose();
    }
}
