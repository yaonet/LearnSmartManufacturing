using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Configuration;
using System.Data;
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
                .ConfigureServices((context, services) =>
                {
                    // Register your services here
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
                    services.AddTransient<MotionControlViewModel>();
                })
                .Build();
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            await _host.StartAsync();
            base.OnStartup(e);

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.DataContext = _host.Services.GetRequiredService<MainWindowViewModel>();
            mainWindow.Show();
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            await _host.StopAsync();
            _host.Dispose();
            base.OnExit(e);
        }

    }

}
