using Avalonia;
using ReactiveUI;
using ReactiveUI.Avalonia;
using OscVisualizer.ViewModels;
using OscVisualizer.Views;
using System;
using System.Reflection;

namespace OscVisualizer.Desktop;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .UseReactiveUI(rxAppBuilder =>
            {
                // Enable ReactiveUI
                rxAppBuilder
                  .WithViewsFromAssembly(Assembly.GetExecutingAssembly())
                  .WithRegistration(locator =>
                  {
                      // Register your services here
                      locator.RegisterLazySingleton(() => new MainWindow());
                  });
            }).RegisterReactiveUIViewsFromEntryAssembly();
}
