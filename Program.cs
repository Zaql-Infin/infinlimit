using System;
using Velopack;

namespace InfinLimit
{
    public class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // MUST be first — Velopack applies pending updates before the UI loads
            VelopackApp.Build().Run();

            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
    }
}
