using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Text.RegularExpressions;

namespace TwinShift
{
    internal static class Program
    {
        public static int Main(string[] args)
        {
            // Empty stdout, including on errors: this plugin never returns a permission decision.
            try
            {
                if (args.Length != 0 || !File.Exists(SignalBus.EndpointFile))
                    return 0;
                string json = ReadInput(Console.OpenStandardInput());
                if (json == null)
                    return 0;
                var data = new JavaScriptSerializer
                {
                    MaxJsonLength = 262144,
                    RecursionLimit = 32
                }.DeserializeObject(json) as Dictionary<string, object>;
                if (data == null)
                    return 0;
                string kind = Classify(data);
                if (kind == null)
                    return 0;
                string session = Field(data, "session_id"), turn = Field(data, "turn_id"), call = Field(data, "tool_use_id");
                if (session.Length == 0)
                    return 0;
                string unique = String.Join("/", session, turn, call, Field(data, "hook_event_name"));
                if (turn.Length == 0 && call.Length == 0)
                    unique += "/" + Guid.NewGuid().ToString("N");
                SignalBus.Send(kind, unique, session);
            }
            catch (Exception)
            { /* Optional notifications must never block the user's work. */
            }

            return 0;
        }

        internal static string ReadInput(Stream stream)
        {
            using (var ms = new MemoryStream())
            {
                byte[] chunk = new byte[8192];
                int n;
                while ((n = stream.Read(chunk, 0, chunk.Length)) > 0)
                {
                    if (ms.Length + n > 262144)
                        return null;
                    ms.Write(chunk, 0, n);
                }

                byte[] bytes = ms.ToArray();
                int offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
                return new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
            }
        }

        static string Field(Dictionary<string, object> d, string k) => d != null && d.TryGetValue(k, out var v) ? v as string ?? "" : "";
        static Dictionary<string, object> Obj(Dictionary<string, object> d, string k) => d.TryGetValue(k, out var v) ? v as Dictionary<string, object> : null;
        internal static string Classify(Dictionary<string, object> data)
        {
            string tool = Field(data, "tool_name");
            switch (Field(data, "hook_event_name"))
            {
                case "PermissionRequest":
                    return "permission";
                case "PreToolUse":
                    return Regex.IsMatch(tool, @"\A(?:functions\.)?request_user_input(?:_async)?\z") ? "question" : null;
                case "Stop":
                    return "ended";
                case "Interrupt":
                    return "stopped";
                case "PostToolUse":
                    if (tool != "Bash")
                        return null;
                    return TestReport(Field(Obj(data, "tool_input"), "command"), data);
                default:
                    return null;
            }
        }

        static readonly Regex DirectCommand = new Regex(@"\A(?<runner>(?:(?:pnpm exec|npx|npm exec --) )?playwright test|node --test)(?: +[A-Za-z0-9_./\\-]+)*\z", RegexOptions.CultureInvariant);
        static readonly Regex EscapeCodes = new Regex(@"\x1b\[[0-9;?]*[A-Za-z]");
        static readonly Regex PlaywrightEnd = new Regex(@"(?:\A|\n)\s*([1-9][0-9]*) passed \([\d.hms ]+\)\s*\z");
        static readonly Regex PlaywrightFailed = new Regex(@"(?m)^\s*[1-9][0-9]* failed\s*$");
        static readonly Regex PlaywrightStart = new Regex(@"(?m)^\s*Running [1-9][0-9]* tests? using [1-9][0-9]* workers?\s*$");
        static readonly Regex NodeEnd = new Regex(@"(?:^|\n)(?:#|ℹ) (?:duration_ms|duration) [0-9.]+(?:ms)?\s*\z");
        internal static string TestReport(string command, Dictionary<string, object> data)
        {
            if (String.IsNullOrEmpty(command) || command.Length > 1024)
                return null;
            var match = DirectCommand.Match(command.Trim());
            if (!match.Success)
                return null;
            // Restrict options to plain file operands. Discovery, wrappers, shell pipelines and scripts are intentionally quiet.
            string args = command.Substring(command.IndexOf(match.Groups["runner"].Value, StringComparison.Ordinal) + match.Groups["runner"].Length);
            if (Regex.IsMatch(args, @"(?:^|\s)-"))
                return null;
            int? exitCode = null;
            string output = Field(data, "tool_response");
            var result = Obj(data, "tool_response");
            if (result != null)
            {
                if (result.ContainsKey("session_id") && result["session_id"] != null)
                    return null;
                if (!result.TryGetValue("exit_code", out var code) || !(code is int))
                    return null;
                exitCode = (int)code;
                output = Field(result, "output");
            }

            if (output.Length == 0)
                return null;
            output = EscapeCodes.Replace(output, "").Replace("\r", "\n");
            if (output.IndexOf("interrupted", StringComparison.OrdinalIgnoreCase) >= 0 || output.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0)
                return null;
            bool failed, passed;
            if (match.Groups["runner"].Value == "node --test")
            {
                if (!NodeEnd.IsMatch(output))
                    return null;
                failed = Regex.IsMatch(output, @"(?m)^(?:#|ℹ) fail [1-9][0-9]*\s*$");
                passed = Regex.IsMatch(output, @"(?m)^(?:#|ℹ) tests [1-9][0-9]*\s*$") && Regex.IsMatch(output, @"(?m)^(?:#|ℹ) pass [1-9][0-9]*\s*$") && Regex.IsMatch(output, @"(?m)^(?:#|ℹ) fail 0\s*$") && Regex.IsMatch(output, @"(?m)^(?:#|ℹ) cancelled 0\s*$");
            }
            else
            {
                if (!PlaywrightStart.IsMatch(output))
                    return null;
                failed = PlaywrightFailed.IsMatch(output);
                passed = PlaywrightEnd.IsMatch(output) && !Regex.IsMatch(output, @"(?im)^\s*(?:[1-9][0-9]* (?:flaky|did not run)|.*error outside of any test).*$");
            }

            if (failed)
                return exitCode == 0 ? null : "failed";
            return passed && (!exitCode.HasValue || exitCode == 0) ? "passed" : null;
        }
    }
}
