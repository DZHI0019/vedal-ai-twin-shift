using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TwinShift
{
    internal sealed class Receiver : IDisposable
    {
        readonly string name = "twinshift-" + Guid.NewGuid().ToString("N");
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        readonly PipeSecurity acl = new PipeSecurity();
        internal event Action<string, string, string> Received;
        public Receiver()
        {
            var sid = WindowsIdentity.GetCurrent().User;
            acl.SetOwner(sid);
            acl.SetAccessRuleProtection(true, false);
            acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
            acl.AddAccessRule(new PipeAccessRule(sid, PipeAccessRights.FullControl, AccessControlType.Allow));
        }

        internal void Start()
        {
            // Boot the listener before advertising it. One client at a time, one bounded message per connection.
            var first = Create();
            Files.AtomicText(SignalBus.EndpointFile, name);
            Task.Run(() => Listen(first, lifetime.Token)).ContinueWith(t => System.Diagnostics.Trace.TraceError("Twin Shift listener stopped: {0}", t.Exception.GetBaseException()), TaskContinuationOptions.OnlyOnFaulted);
        }

        NamedPipeServerStream Create() => new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 128, 128, acl);
        async Task Listen(NamedPipeServerStream initial, CancellationToken stop)
        {
            var pipe = initial;
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    using (pipe = pipe ?? Create())
                    using (stop.Register(pipe.Dispose))
                    {
                        await pipe.WaitForConnectionAsync(stop).ConfigureAwait(false);
                        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop))
                        {
                            timeout.CancelAfter(750);
                            using (timeout.Token.Register(pipe.Dispose))
                            {
                                byte[] data = new byte[97];
                                int n = 0;
                                while (n < data.Length && (n == 0 || data[n - 1] != 10))
                                {
                                    int part = await pipe.ReadAsync(data, n, data.Length - n, timeout.Token).ConfigureAwait(false);
                                    if (part == 0)
                                        break;
                                    n += part;
                                }

                                if (n > 0 && data[n - 1] == 10 && SignalBus.Parse(Encoding.ASCII.GetString(data, 0, n - 1), out var kind, out var id, out var scope))
                                {
                                    Received?.Invoke(kind, id, scope);
                                    await pipe.WriteAsync(new byte[] { 1 }, 0, 1, timeout.Token).ConfigureAwait(false);
                                }
                            }
                        }
                    }
                }
                catch (Exception e)when (e is IOException || e is ObjectDisposedException || e is OperationCanceledException || e is UnauthorizedAccessException)
                {
                    System.Diagnostics.Trace.TraceInformation("Twin Shift pipe: {0} (0x{1:X8})", e.GetType().Name, e.HResult);
                    if (!stop.IsCancellationRequested)
                        await Task.Delay(15).ConfigureAwait(false);
                }

                pipe = null;
            }
        }

        public void Dispose()
        {
            lifetime.Cancel();
            try
            {
                if (File.Exists(SignalBus.EndpointFile) && File.ReadAllText(SignalBus.EndpointFile) == name)
                    File.Delete(SignalBus.EndpointFile);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
