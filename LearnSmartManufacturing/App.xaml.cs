using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Windows;
using LearnSmartManufacturing.Services;
using LearnSmartManufacturing.ViewModels;

namespace LearnSmartManufacturing
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private readonly IHost _host;

        public App()
        {
            _host = Host.CreateDefaultBuilder()
                // The working directory is not the app directory when launched from a shortcut.
                .UseContentRoot(AppContext.BaseDirectory)
                .ConfigureServices((_, services) =>
                {
                    services.AddSingleton<MainWindow>();
                    services.AddSingleton<MainWindowViewModel>();

                    services.AddSingleton<INavigationService, NavigationService>();
                    services.AddTransient<HomeViewModel>();
                    services.AddTransient<SettingsViewModel>();
                    services.AddTransient<AsyncProgrammingViewModel>();
                    services.AddTransient<MvvmViewModel>();
                    services.AddTransient<ModbusViewModel>();
                    services.AddTransient<EtherCatViewModel>();
                    services.AddTransient<MqttViewModel>();
                    services.AddTransient<IndustrialCameraViewModel>();
                    services.AddTransient<LensFocalLengthViewModel>();
                                        services.AddTransient<MotionControlViewModel>();
                })
                .Build();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                _host.Start();
                _host.Services.GetRequiredService<MainWindow>().Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"应用程序启动失败：{ex.Message}", "Learn Smart Manufacturing",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Block until the host has stopped; an async void here would let the process exit mid-shutdown.
            try
            {
                _host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            }
            finally
            {
                _host.Dispose();
            }

            base.OnExit(e);
        }
    }
}
