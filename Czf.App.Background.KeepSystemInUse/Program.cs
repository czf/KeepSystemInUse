using System;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Czf.App.Background.KeepSystemInUse
{
    class Program
    {
        static async Task Main(string[] args)
        {
            await CreateHostBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    var config = hostContext.Configuration;
                    services.AddOptions();
                    services.AddHostedService(x => new KeepSystemInUseService(int.Parse(args[0]), DefaultScheduler.Instance));
                })
                .RunConsoleAsync();


            //await host.RunAsync();

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
        public class KeepSystemInUseService : IHostedService
        {
            private readonly int _intervalMinutes;
            private readonly IScheduler _scheduler;
            private IObservable<long> _intervalObservable;
            public KeepSystemInUseService(int intervalMinutes, IScheduler scheduler)
            {
                _intervalMinutes = intervalMinutes;
                _scheduler = scheduler;
            }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                _intervalObservable = Observable.Interval(TimeSpan.FromMinutes(_intervalMinutes), _scheduler);
                _intervalObservable.Subscribe(OnNext, cancellationToken);
                return Task.CompletedTask;
            }

            public void OnNext(long _)
            {
                SetThreadExecutionState(EXECUTION_STATE.ES_DISPLAY_REQUIRED | EXECUTION_STATE.ES_CONTINUOUS);
            }
            public Task StopAsync(CancellationToken cancellationToken)
            {
                SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
                return Task.CompletedTask;
            }

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);

            [FlagsAttribute]
            public enum EXECUTION_STATE : uint
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
