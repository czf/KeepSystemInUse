using System;
using System.IO;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Topshelf;
using Host = Microsoft.Extensions.Hosting.Host;

namespace Czf.App.Background.KeepSystemInUse
{
    class Program
    {
        static ILogger<Program> logger;
        static int? waitValue;
        static async Task Main(string[] args)
        {
            var factory = LoggerFactory.Create(x => x.AddEventLog());
            logger = factory.CreateLogger<Program>();

            //StringBuilder sb = new StringBuilder();
            //foreach (var item in args)
            //{
            //    sb.AppendLine(item);
            //}

            //logger.LogInformation(sb.ToString());

            var configRoot = new ConfigurationBuilder().AddCommandLine(args).Build();
            var serviceValue = configRoot.GetValue<string>("service");
            if (!string.IsNullOrEmpty(serviceValue) || (args.Contains("-servicename") && args.Contains("-displayname")))
            {
                var code = HostFactory.New(x =>
                {
                    x.Service<KeepSystemInUseService>(s =>
                    {
                        int waitMinutes = waitValue ?? configRoot.GetValue<int?>("wait") ?? 7;
                        ApplicationLifetime applicationLifetime = new ApplicationLifetime(null);
                        s.ConstructUsing(n => new KeepSystemInUseService(applicationLifetime, waitMinutes, DefaultScheduler.Instance));
                        s.WhenStarted(y =>
                        {
                            y.StartAsync(applicationLifetime.ApplicationStopped);
                        });
                        s.WhenStopped(z => applicationLifetime.StopApplication());
                    });
                    x.StartAutomaticallyDelayed();
                
                    switch (serviceValue?.ToLowerInvariant())
                    {
                        case "install":
                            x.ApplyCommandLine("install");
                            break;
                        case "uninstall":
                            x.ApplyCommandLine("uninstall");
                            break;
                        case "start":
                            int waitMinutes = configRoot.GetValue<int?>("wait") ?? 10;//doesn't do anything
                            x.ApplyCommandLine($"start");
                            break;
                        case "stop":
                            x.ApplyCommandLine("stop");
                            break;
                        case null:
                            break;
                        default:
                            throw new NotSupportedException("do not support that value");
                    }
                    //x.RunAsLocalSystem();//just to get it installed
                    x.SetDescription("Tells the OS to think its in use.");
                    x.SetDisplayName("Czf.App.Background.KeepSystemInUse");
                    x.SetServiceName("Czf.App.Background.KeepSystemInUse");
                    

                }).Run();
                var exitCode = (int)Convert.ChangeType(code, code.GetTypeCode());
                Environment.ExitCode = exitCode;
                return;
            }

            
            
            await CreateHostBuilder(args)
                .UseWindowsService(o =>
                {
                    o.ServiceName = "Czf.App.Background.KeepSystemInUse";
                })
                .ConfigureServices((hostContext, services) =>
                {
                    var config = hostContext.Configuration;
                    int waitMinutes = config.GetValue<int?>("wait") ?? 7;
                    services.AddOptions();
                    services.AddHostedService(x => new KeepSystemInUseService(x.GetRequiredService<IHostApplicationLifetime>(), waitMinutes, DefaultScheduler.Instance));
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
                logger.LogInformation($"wait: {intervalMinutes}");
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
                var previousState = SetThreadExecutionState(EXECUTION_STATE.ES_DISPLAY_REQUIRED | EXECUTION_STATE.ES_CONTINUOUS);
                
                if(previousState == 0)
                {
                    logger.LogError("SetThreadExecutionState result was NULL");
                }
                else if (previousState.HasFlag(EXECUTION_STATE.ES_DISPLAY_REQUIRED))
                {
                    logger.LogInformation("previous state has display required, thread id:" + Thread.CurrentThread.ManagedThreadId.ToString());
                }
                else
                {
                    logger.LogInformation("previous state didn't have display required, thread id:" + Thread.CurrentThread.ManagedThreadId.ToString());
                }
            }

            public void OnError(Exception e)
            {
                logger.LogError(e,"Observable exception");
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
