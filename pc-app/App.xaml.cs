namespace NexusRemotePC;

public partial class App : System.Windows.Application
{
    public App()
    {
        Startup += (_, _) => AppLogger.Info("Запуск Nexus Remote PC.");
        Exit += (_, _) => AppLogger.Info("Выход из Nexus Remote PC.");
        DispatcherUnhandledException += (_, args) =>
        {
            AppLogger.Error("Необработанная ошибка UI.", args.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                AppLogger.Error("Необработанная ошибка процесса.", exception);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLogger.Error("Необработанная ошибка фоновой задачи.", args.Exception);
            args.SetObserved();
        };
    }
}
