using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Games8thTeamBlocker;

internal static class DriverNetworkVerifier
{
    private const int Port = 7890;

    private static bool TryConnect(int timeoutMs, out string detail)
    {
        using (var client = new TcpClient(AddressFamily.InterNetwork))
        {
            IAsyncResult pending = client.BeginConnect(IPAddress.Loopback, Port, null, null);
            try
            {
                if (!pending.AsyncWaitHandle.WaitOne(timeoutMs))
                {
                    detail = "timeout";
                    return false;
                }
                client.EndConnect(pending);
                detail = "connected";
                return client.Connected;
            }
            catch (Exception ex)
            {
                detail = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
            finally
            {
                pending.AsyncWaitHandle.Close();
            }
        }
    }

    private static int Main()
    {
        TcpListener listener = null;
        string target = Path.GetFullPath(Process.GetCurrentProcess().MainModule.FileName)
                            .Replace('/', '\\');
        Console.WriteLine("PORT=" + Port);
        Console.WriteLine("TARGET=" + target);

        try
        {
            try
            {
                listener = new TcpListener(IPAddress.Loopback, Port);
                listener.Start(8);
                Console.WriteLine("LISTENER=SELF");
            }
            catch (SocketException ex)
            {
                Console.WriteLine("LISTENER=EXISTING;DETAIL=" + ex.SocketErrorCode);
            }

            string detail;
            bool baseline = TryConnect(2000, out detail);
            Console.WriteLine("BASELINE_CONNECT=" + baseline + ";DETAIL=" + detail);
            if (!baseline) return 41;

            if (!KernelDriver.ClearAll())
            {
                Console.WriteLine("CLEAR_BEFORE=False;ERROR=" + KernelDriver.LastErrorMessage);
                return 42;
            }

            if (!KernelDriver.AddBlockedPath(target) ||
                !KernelDriver.ContainsBlockedPath(target))
            {
                Console.WriteLine("PATH_BIND=False;ERROR=" + KernelDriver.LastErrorMessage);
                return 43;
            }
            Console.WriteLine("PATH_BIND=True");

            Thread.Sleep(250);
            bool blockedConnect = TryConnect(2000, out detail);
            Console.WriteLine("BLOCKED_CONNECT=" + blockedConnect + ";DETAIL=" + detail);
            if (blockedConnect) return 44;

            if (!KernelDriver.ClearAll())
            {
                Console.WriteLine("CLEAR_AFTER=False;ERROR=" + KernelDriver.LastErrorMessage);
                return 45;
            }

            Thread.Sleep(250);
            bool restored = TryConnect(2000, out detail);
            Console.WriteLine("RESTORED_CONNECT=" + restored + ";DETAIL=" + detail);
            if (!restored) return 46;

            Console.WriteLine("NETWORK_BLOCK_VERIFIED=True");
            return 0;
        }
        finally
        {
            bool cleared = KernelDriver.ClearAll();
            Console.WriteLine("FINAL_CLEAR=" + cleared + ";ERROR=" + KernelDriver.LastErrorMessage);
            if (listener != null) listener.Stop();
        }
    }
}
