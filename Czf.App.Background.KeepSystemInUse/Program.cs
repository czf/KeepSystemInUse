using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IWshRuntimeLibrary;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using File = System.IO.File;
using Host = Microsoft.Extensions.Hosting.Host;

namespace Czf.App.Background.KeepSystemInUse
{
    class Program
    {
        const string CLI_COMMAND_NAME = "keepinuse";
        static ILogger<Program> logger;
        static async Task Main(string[] args)
        {
            var factory = LoggerFactory.Create(x => x.AddEventLog());
            logger = factory.CreateLogger<Program>();


            if (args.Contains("--startup"))
            {
                CreateStartupShortcut();
                return;
            }
            await CreateHostBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    var config = hostContext.Configuration;
                    
                    services.AddOptions();
                    services.AddHostedService(x => new KeepSystemInUseService(x.GetRequiredService<IHostApplicationLifetime>()));
                })
                .RunConsoleAsync();
        }

        private static void CreateStartupShortcut()
        {
            var dotNetCliPath = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + "\\dotnet\\dotnet.exe";


            if (File.Exists(dotNetCliPath))
            {
                var startupPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                CreateShortcut(Process.GetCurrentProcess().ProcessName, startupPath, CLI_COMMAND_NAME);
            }
            else
            {
                Console.Error.WriteLine("Couldn't find dotnet.exe, can't create startup shortcut");
            }
        }

        static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((hostingContext, config) =>
            {
                config.Sources.Clear();
                config
                .AddJsonFile("appsettings.json",true)
                .AddCommandLine(args);
            });

        private static void CreateShortcut(string shortcutName, string shortcutPath, string targetFileLocation)
        {
            string shortcutLocation = System.IO.Path.Combine(shortcutPath, shortcutName + ".lnk");
            WshShell shell = new WshShell();
            IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(shortcutLocation);

            shortcut.Description = $"shortcut to {targetFileLocation}";   // The description of the shortcut
            shortcut.TargetPath = targetFileLocation;                 // The path of the file that will launch when the shortcut is run
            shortcut.Save();                                    // Save the shortcut
        }

        private class KeepSystemInUseService : IHostedService
        {
            private bool disposedValue;
            SemaphoreSlim _wait;
            private CancellationTokenSource _linkedTokenSource;
            private IHostApplicationLifetime _applicationLifetime;
            private Task _inUseTask;
            public KeepSystemInUseService(IHostApplicationLifetime applicationLifetime )
            {
                _applicationLifetime = applicationLifetime;
                _wait = new SemaphoreSlim(0,1);
                
                _linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_applicationLifetime.ApplicationStopping, _applicationLifetime.ApplicationStopped);
                _linkedTokenSource.Token.Register(ContinueWaitSemaphore);
            }

            private void ContinueWaitSemaphore() 
            {
                _wait.Release();
            }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    _inUseTask = Task.Run(LongRunningAction);
                    
                }
                return Task.CompletedTask;
            }

            private Task LongRunningAction()
            {
                SetThreadExecutionState(EXECUTION_STATE.ES_DISPLAY_REQUIRED | EXECUTION_STATE.ES_CONTINUOUS);
                return _wait.WaitAsync();
            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                try
                {
                    _wait.Release();
                }
                finally
                {
                    SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
                    _applicationLifetime.StopApplication();
                }
                return Task.CompletedTask;
            }

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);

            public void Dispose()
            {
                Dispose(disposing: true);
                GC.SuppressFinalize(this);
            }
            protected virtual void Dispose(bool disposing)
            {
                if (!disposedValue)
                {
                    if (disposing)
                    {
                        _linkedTokenSource.Dispose();
                        _wait.Dispose();
                    }

                    disposedValue = true;
                }
            }

            [Flags]
            private enum EXECUTION_STATE : uint
            {
                ES_AWAYMODE_REQUIRED = 0x00000040,
                ES_CONTINUOUS = 0x80000000,
                ES_DISPLAY_REQUIRED = 0x00000002,
                ES_SYSTEM_REQUIRED = 0x00000001
                // Legacy flag, should not be used.
                // ES_USER_PRESENT = 0x00000004
            }
        }
    }
}
