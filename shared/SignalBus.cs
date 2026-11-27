using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace TwinShift
{
    internal static class SignalBus
    {
        public static readonly string Root = ResolveDataDirectory(Environment.GetEnvironmentVariable("TWINSHIFT_DATA_DIR"));
        public static string EndpointFile => Path.Combine(Root, "receiver.txt");

        internal static string ResolveDataDirectory(string configured)
        {
            if (string.IsNullOrWhiteSpace(configured))
                configured = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TwinShift", "Data");
            return Path.GetFullPath(configured);
        }

        public static bool ValidPipe(string name) => Regex.IsMatch(name ?? "", @"\Atwinshift-[a-f0-9]{32}\z");
        static readonly string[] Kinds =
        {
            "permission",
            "question",
            "passed",
            "failed",
            "ended",
            "stopped",
            "show"
        };
        public static string Hash(string value)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }

        public static bool Parse(string packet, out string kind, out string id, out string scope)
        {
            kind = id = scope = "";
            if (packet == null || packet.Length > 96)
                return false;
            var p = packet.Split('|');
            if (p.Length != 4 || p[0] != "TS1" || !Kinds.Contains(p[1]) || !Regex.IsMatch(p[2], @"\A[0-9a-f]{32}\z") || !Regex.IsMatch(p[3], @"\A[0-9a-f]{12}\z"))
                return false;
            kind = p[1];
            id = p[2];
            scope = p[3];
            return true;
        }

        public static void Send(string kind, string identity, string scope)
        {
            // A missing receiver is normal. The hook never starts an app, asks for approval or queues disk events.
            try
            {
                if (!Kinds.Contains(kind) || !File.Exists(EndpointFile) || new FileInfo(EndpointFile).Length > 100)
                    return;
                string name = File.ReadAllText(EndpointFile).Trim();
                if (!ValidPipe(name))
                    return;
                using (var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification))
                {
                    pipe.Connect(100);
                    string packet = "TS1|" + kind + "|" + Hash(kind + "/" + identity).Substring(0, 32) + "|" + Hash(scope).Substring(0, 12);
                    byte[] bytes = Encoding.ASCII.GetBytes(packet + "\n");
                    pipe.Write(bytes, 0, bytes.Length);
                    var receipt = pipe.ReadAsync(new byte[1], 0, 1);
                    try
                    {
                        receipt.Wait(300);
                    }
                    catch (AggregateException)
                    {
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (TimeoutException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
