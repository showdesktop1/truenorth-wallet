using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;

namespace TrueNorthWallet;

public static class SingleInstance
{
    private static string PipeName => "TrueNorthWallet-" + Environment.UserName;

    private static bool AllowMultiple => Environment.GetEnvironmentVariable("TRUENORTH_ALLOW_MULTIPLE") == "1";

    // Runs automatically before Main. If a copy of the wallet is already running,
    // hand it our link (or just ask it to come forward) and exit.
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void ForwardToRunningCopy()
    {
        if (AllowMultiple) return;
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(200);

            var link = Environment.GetCommandLineArgs()
                .FirstOrDefault(a => a.StartsWith("truenorth:", StringComparison.OrdinalIgnoreCase)) ?? "ACTIVATE";

            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(link);
            writer.Flush();
            Environment.Exit(0);
        }
        catch
        {
            // nobody is listening, so this is the first copy
        }
    }

    // Called by the first copy to listen for links from later launches.
    public static void StartServer(Action<string> onMessage)
    {
        if (AllowMultiple) return;

        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte);
                    server.WaitForConnection();
                    using var reader = new StreamReader(server);
                    var buffer = new char[4096];
                    var count = reader.Read(buffer, 0, buffer.Length);
                    var text = new string(buffer, 0, count);
                    var line = text.Split('\n')[0].Trim();
                    if (line.Length > 0) onMessage(line);
                }
                catch
                {
                    Thread.Sleep(500);
                }
            }
        }) { IsBackground = true };
        thread.Start();
    }
}
