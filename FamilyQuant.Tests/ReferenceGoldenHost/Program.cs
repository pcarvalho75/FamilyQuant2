using System;

namespace FamilyQuant.Tests
{
    internal static class ReferenceGoldenHost
    {
        [STAThread]
        private static int Main(string[] args)
        {
            var app = new App();
            app.GetType().GetMethod("InitializeComponent")?.Invoke(app, null);
            int failures = 0;
            GoldenNumbers.Run(args, (name, passed, detail) =>
            {
                Console.WriteLine((passed ? "PASS " : "FAIL ") + name + (detail == null ? "" : ": " + detail));
                if (!passed) failures++;
            });
            return failures == 0 ? 0 : 1;
        }
    }
}
