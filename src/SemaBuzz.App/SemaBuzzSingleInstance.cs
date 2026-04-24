using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace SemaBuzz.App;

/// <summary>
/// Enforces a single running instance of SemaBuzz.
///
/// When a second instance is launched (e.g. by clicking a buzz:// link):
///   1. It detects the first instance is already running via a named Mutex.
///   2. It forwards the command-line argument (the buzz:// URI) to the first
///      instance over a named pipe.
///   3. It exits immediately.
///
/// The first instance listens on the pipe and raises <see cref="UriReceived"/>
/// when a URI arrives from a secondary launch.
/// </summary>
internal static class SemaBuzzSingleInstance
{
    private const string MutexName = "SemaBuzz_SingleInstance_Mutex";
    private const string PipeName  = "SemaBuzz_SingleInstance_Pipe";

    private static Mutex? _mutex;

    // Raised on the thread-pool when a buzz:// URI is forwarded from a second instance.
    // Subscribers must marshal to the UI thread themselves.
    public static event Action<string>? UriReceived;

    // Raised when a secondary instance starts without a URI (user double-clicked the exe).
    public static event Action? FocusRequested;

    //  First-instance check

    /// <summary>
    /// Returns true if this is the first (primary) instance.
    /// If false, the caller should forward <paramref name="args"/> to the
    /// primary instance via <see cref="ForwardToPrimary"/> and then exit.
    /// </summary>
    public static bool ClaimPrimary()
    {
        try
        {
            _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
            if (createdNew) return true;

            // Mutex already existed. Try to acquire with zero timeout.
            try
            {
                if (_mutex.WaitOne(0)) return true; // unowned  we got it
            }
            catch (AbandonedMutexException)
            {
                return true; // previous holder was force-killed; we own it now
            }

            // Mutex is held by another process. Verify it's actually alive by
            // probing the named pipe with a short timeout. If it doesn't answer,
            // it's a zombie holding the mutex  we proceed as primary anyway.
            try
            {
                using var probe = new System.IO.Pipes.NamedPipeClientStream(
                    ".", PipeName, System.IO.Pipes.PipeDirection.Out);
                probe.Connect(timeout: 500);
                return false; // pipe answered  a real primary is running
            }
            catch
            {
                return true; // no pipe server  zombie process; we become primary
            }
        }
        catch (AbandonedMutexException)
        {
            return true; // constructor itself acquired an abandoned mutex
        }
    }

    //  Secondary instance

    // Sent to the primary when a secondary instance is launched with no URI,
    // so the primary window still gets focused.
    private const string FocusSignal = "__focus__";

    /// <summary>
    /// Sends <paramref name="argument"/> to the primary instance over the named pipe.
    /// Pass <see langword="null"/> to send only a focus signal.
    /// </summary>
    public static void ForwardToPrimary(string? argument)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName,
                PipeDirection.Out, PipeOptions.WriteThrough);
            pipe.Connect(timeout: 500);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            string messageToSend;
            if (argument != null)
                messageToSend = argument;
            else
                messageToSend = FocusSignal;
            writer.WriteLine(messageToSend);
            writer.Flush();
            pipe.WaitForPipeDrain();
        }
        catch { /* primary may have exited  ignore */ }
    }

    //  Primary instance listener

    /// <summary>
    /// Starts listening for URIs forwarded from secondary instances.
    /// Runs a background thread; safe to call once from <c>App.OnStartup</c>.
    /// </summary>
    public static void StartListening(CancellationToken appExiting)
    {
        var thread = new Thread(() => ListenLoop(appExiting))
        {
            IsBackground = true,
            Name         = "BuzzUri-PipeListener",
        };
        thread.Start();
    }

    private static void ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(PipeName,
                    PipeDirection.In, maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte, PipeOptions.None);

                pipe.WaitForConnection();

                using var reader = new StreamReader(pipe);
                var rawLine = reader.ReadLine();
                string? line = null;
                if (rawLine != null)
                    line = rawLine.Trim();
                if (line == FocusSignal || string.IsNullOrWhiteSpace(line))
                {
                    var focusHandler = FocusRequested;
                    if (focusHandler != null)
                        focusHandler();
                }
                else
                {
                    var uriHandler = UriReceived;
                    if (uriHandler != null)
                        uriHandler(line);
                }
            }
            catch (OperationCanceledException) { break; }
            catch { /* pipe error  restart listener */ }
        }
    }
}
