using System;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace PlcToHmi
{
    /// <summary>
    /// 应用程序入口类
    /// 负责全局编码提供器注册、命令行测试入口与全局未捕获异常监控
    /// </summary>
    public class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // 尽早注册扩展编码支持（GBK、GB18030、Big5 等），确保多线程与首屏即可安全解码
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // 支持命令行 --test 参数进行无头自动化测试
            if (args.Length > 0 && args[0].Equals("--test", StringComparison.OrdinalIgnoreCase))
            {
                int exitCode = PlcToHmiSelfTest.RunAllTestsAsync().GetAwaiter().GetResult();
                Environment.Exit(exitCode);
                return;
            }

            // 监听非 UI 线程未捕获异常
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                string msg = e.ExceptionObject is Exception ex ? ex.Message : e.ExceptionObject?.ToString() ?? "未知错误";
                MessageBox.Show($"应用程序发生非受控异常，即将退出：\n{msg}", "系统严重错误", MessageBoxButton.OK, MessageBoxImage.Error);
            };

            // 监听未观察到的 Task 任务异常
            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                e.SetObserved(); // 阻止进程由于后台任务异常直接崩溃
            };

            try
            {
                var app = new Application();

                // 监听 WPF UI 调度线程异常，优雅提示并恢复，避免界面瞬间闪退
                app.DispatcherUnhandledException += (sender, e) =>
                {
                    MessageBox.Show(
                        $"界面操作发生异常：\n{e.Exception.Message}\n\n堆栈详情：\n{e.Exception.StackTrace}",
                        "操作异常",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );
                    e.Handled = true; // 拦截异常，保持主程序继续运行
                };

                var mainWindow = new MainWindow();
                app.Run(mainWindow);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"程序启动初始化失败：\n{ex}", "启动致命错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
