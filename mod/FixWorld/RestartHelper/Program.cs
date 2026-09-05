// SPDX-License-Identifier: MPL-2.0
using System;
using System.IO;
using FixWorld.Bootstrap;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        { Restart.RunHelper(args); return 0; }
        catch (Exception error)
        {
            try
            { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FixWorld.Restart.log"), DateTime.UtcNow.ToString("O") + " " + error + Environment.NewLine); }
            catch { Console.Error.WriteLine(error); }
            return 1;
        }
    }
}
