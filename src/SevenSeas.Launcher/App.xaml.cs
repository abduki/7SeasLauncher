using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using SevenSeas.Core.Abstractions;
using SevenSeas.Core.Services;
using SevenSeas.Launcher.Services;
using SevenSeas.Launcher.ViewModels;
using SevenSeas.Launcher.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;
using Serilog;

namespace SevenSeas.Launcher;

/// <summary>
/// The composition root. Builds the object graph, starts the logger, brings up the
/// infrastructure services and shows the shell.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;

    public static IServiceProvider Services { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppPaths.EnsureCreated();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(AppPaths.LogsFolder, "7seas-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .CreateLogger();

        Log.Information("7SeasLauncher starting.");

        try
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            _services = services.BuildServiceProvider();
            Services = _services;

            // Infrastructure comes up before any UI exists.
            var settings = _services.GetRequiredService<ISettingsService>();
            settings.Load();

            var repository = _services.GetRequiredService<SqliteGameRepository>();
            repository.Initialize();

            _services.GetRequiredService<TrashService>().CleanupExpired();
            _services.GetRequiredService<ISchemeLoader>().LoadAll();

            // One-time: the retired "home pages" list becomes browse-only bookmarks.
            BookmarkMigration.Migrate(
                settings,
                _services.GetRequiredService<ISchemeLoader>(),
                _services.GetRequiredService<ILoggerFactory>().CreateLogger("BookmarkMigration"));

            DispatcherUnhandledException += OnDispatcherUnhandledException;

            var window = _services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();

            var pipeline = _services.GetRequiredService<PipelineWorker>();
            await pipeline.StartAsync();
            await pipeline.ResumeInterruptedAsync();

            // One-time reminder about antivirus exclusions, dismissable for good.
            ShowAntivirusReminderIfNeeded(window, settings);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "7SeasLauncher failed to start.");
            Dialogs.Inform(
                "7SeasLauncher could not start",
                ex.Message + "\n\nSee the logs in " + AppPaths.LogsFolder);
            Shutdown(1);
        }
    }

    private void ShowAntivirusReminderIfNeeded(Window owner, ISettingsService settings)
    {
        if (settings.Current.SuppressAntivirusReminder)
        {
            return;
        }

        var exclusions = _services!.GetRequiredService<AntivirusExclusionService>();
        if (exclusions.RecommendedFolders().Count == 0)
        {
            // Nothing configured yet, so there is nothing useful to suggest.
            return;
        }

        var reminder = new AntivirusExclusionDialog(exclusions) { Owner = owner };
        reminder.ShowDialog();

        if (reminder.DoNotShowAgain)
        {
            settings.Update(s => s.SuppressAntivirusReminder = true);
            Log.Information("Antivirus reminder dismissed permanently.");
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(Log.Logger, dispose: false);
        });

        // Settings Service.
        services.AddSingleton<ISettingsService>(sp => new JsonSettingsService(
            AppPaths.SettingsFile, sp.GetRequiredService<ILogger<JsonSettingsService>>()));

        // Job Queue and Library Repository share one SQLite file.
        services.AddSingleton(sp => new SqliteGameRepository(
            AppPaths.DatabaseFile, sp.GetRequiredService<ILogger<SqliteGameRepository>>()));
        services.AddSingleton<IJobRepository>(sp => sp.GetRequiredService<SqliteGameRepository>());
        services.AddSingleton<IGameRepository>(sp => sp.GetRequiredService<SqliteGameRepository>());

        // Scheme Loader, also serving as the password-hint source.
        services.AddSingleton(sp => new SchemeLoader(
            AppPaths.SchemesFolder, sp.GetRequiredService<ILogger<SchemeLoader>>()));
        services.AddSingleton<ISchemeLoader>(sp => sp.GetRequiredService<SchemeLoader>());
        services.AddSingleton<IArchivePasswordSource>(sp => sp.GetRequiredService<SchemeLoader>());

        // The processing pipeline.
        services.AddSingleton<IClock>(SystemClock.Instance);
        services.AddSingleton<IArchiveVerifier, ArchiveVerifier>();
        services.AddSingleton<IArchiveExtractor, ArchiveExtractor>();
        services.AddSingleton<IExecutableFinder, ExecutableFinder>();
        services.AddSingleton<IAsyncPolicy<HttpResponseMessage>>(_ => Policy
            .Handle<HttpRequestException>()
            .OrResult<HttpResponseMessage>(r => (int)r.StatusCode >= 500)
            .WaitAndRetryAsync(3, attempt => TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt))));
        services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
        services.AddSingleton<IMetadataService, MetadataService>();

        // Foundation + pipeline.
        services.AddSingleton<TrashService>();
        services.AddSingleton<SpaceCleanupService>();
        services.AddSingleton<DownloadIntakeService>();
        services.AddSingleton<PipelineWorker>();

        // UI-side services.
        services.AddSingleton<StatusService>();
        services.AddSingleton<ShellNavigator>();
        services.AddSingleton<AntivirusExclusionService>();
        services.AddSingleton<IUserInteraction, WpfUserInteraction>();
        services.AddSingleton<DownloadInterceptor>();
        services.AddSingleton<DownloadsFolderWatcher>();
        services.AddSingleton<DownloadPopupService>();
        services.AddSingleton<GameLauncherService>();
        services.AddSingleton<DiagnosticsExporter>();

        // View models and views.
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<BrowserViewModel>();
        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<JobsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<BrowserView>();
        services.AddSingleton<LibraryView>();
        services.AddSingleton<JobsView>();
        services.AddSingleton<SettingsView>();
        services.AddSingleton<MainWindow>();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled UI exception.");
        Dialogs.Inform("Something went wrong", e.Exception.Message);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            // Never leave a download window behind on shutdown.
            _services?.GetService<DownloadPopupService>()?.CloseAll();

            // PipelineWorker is IAsyncDisposable, so the container must be disposed asynchronously.
            _services?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during shutdown.");
        }
        finally
        {
            Log.Information("7SeasLauncher stopped.");
            Log.CloseAndFlush();
        }

        base.OnExit(e);
    }
}
