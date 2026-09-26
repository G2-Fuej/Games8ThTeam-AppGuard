using System;
using System.Diagnostics;
using System.IO;
using Games8thTeamBlocker;

internal static class DriverIoctlVerifier
{
    private static int Main()
    {
        string target = Process.GetCurrentProcess().MainModule.FileName;
        target = Path.GetFullPath(target).Replace('/', '\\');
        Console.WriteLine("DEVICE=\\\\.\\G8TGuard");
        Console.WriteLine("TARGET=" + target);

        try
        {
            string[] before;
            string error;
            bool queryBefore = KernelDriver.TryQueryBlockedPaths(out before, out error);
            Console.WriteLine("QUERY_BEFORE=" + queryBefore + ";COUNT=" + before.Length);
            if (!queryBefore)
            {
                Console.WriteLine("ERROR=" + error);
                return 31;
            }

            if (!KernelDriver.ClearAll())
            {
                Console.WriteLine("CLEAR_BEFORE=False;ERROR=" + KernelDriver.LastErrorMessage);
                return 32;
            }
            Console.WriteLine("CLEAR_BEFORE=True");

            bool added = KernelDriver.AddBlockedPath(target);
            Console.WriteLine("ADD=" + added + ";ERROR=" + KernelDriver.LastErrorMessage);
            if (!added) return 33;

            string[] afterAdd;
            bool queried = KernelDriver.TryQueryBlockedPaths(out afterAdd, out error);
            Console.WriteLine("QUERY_AFTER_ADD=" + queried + ";COUNT=" + afterAdd.Length);
            if (!queried)
            {
                Console.WriteLine("ERROR=" + error);
                return 34;
            }

            foreach (string path in afterAdd)
                Console.WriteLine("BOUND_PATH=" + path);
            bool exact = KernelDriver.ContainsBlockedPath(target);
            Console.WriteLine("EXACT_PATH_BOUND=" + exact);
            if (!exact) return 35;

            bool removed = KernelDriver.RemoveBlockedPath(target);
            Console.WriteLine("REMOVE=" + removed + ";ERROR=" + KernelDriver.LastErrorMessage);
            if (!removed) return 36;

            string[] afterRemove;
            queried = KernelDriver.TryQueryBlockedPaths(out afterRemove, out error);
            Console.WriteLine("QUERY_AFTER_REMOVE=" + queried + ";COUNT=" + afterRemove.Length);
            if (!queried || afterRemove.Length != 0)
            {
                Console.WriteLine("ERROR=" + error);
                return 37;
            }

            Console.WriteLine("IOCTL_VERIFIED=True");
            return 0;
        }
        finally
        {
            bool cleared = KernelDriver.ClearAll();
            Console.WriteLine("FINAL_CLEAR=" + cleared + ";ERROR=" + KernelDriver.LastErrorMessage);
        }
    }
}
