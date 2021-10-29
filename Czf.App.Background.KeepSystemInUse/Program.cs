using System;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Czf.App.Background.KeepSystemInUse
{
    class Program
    {
        static async Task Main(string[] args)
        {
            int minutes = args.Length < 1 ? 7 : int.Parse(args[0]);

            await CreateHostBuilder(args)
                .UseWindowsService(o =>
                {
                    o.ServiceName = "Czf.App.Background.KeepSystemInUse";
                })
                .ConfigureServices((hostContext, services) =>
                {
                    var config = hostContext.Configuration;
                    services.AddOptions();
                    services.AddHostedService(x => new KeepSystemInUseService(x.GetRequiredService<IHostApplicationLifetime>(), minutes, DefaultScheduler.Instance));
                })
                .RunConsoleAsync();
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



        private class KeepSystemInUseService : IHostedService
        {
            private bool disposedValue;
            private readonly int _intervalMinutes;
            private readonly IScheduler _scheduler;
            private IObservable<long> _intervalObservable;
            private CancellationTokenSource _linkedTokenSource;
            private IHostApplicationLifetime _applicationLifetime;
            public KeepSystemInUseService(IHostApplicationLifetime applicationLifetime, int intervalMinutes, IScheduler scheduler)
            {
                _applicationLifetime = applicationLifetime;
                _intervalMinutes = intervalMinutes;
                _scheduler = scheduler;
                _linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_applicationLifetime.ApplicationStopping, _applicationLifetime.ApplicationStopped);
            }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    _intervalObservable = Observable.Interval(TimeSpan.FromMinutes(_intervalMinutes), _scheduler);
                    _intervalObservable.Subscribe(OnNext,OnError, _linkedTokenSource.Token);
                }
                return Task.CompletedTask;
            }

            public void OnNext(long _)
            {
                SetThreadExecutionState(EXECUTION_STATE.ES_DISPLAY_REQUIRED | EXECUTION_STATE.ES_CONTINUOUS);
            }

            public void OnError(Exception e)
            {
                Console.Error.WriteLine($"exception: {e.Message}");
                _applicationLifetime.StopApplication();
            }
            public Task StopAsync(CancellationToken cancellationToken)
            {
                try
                {
                    SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
                }
                finally
                {
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
