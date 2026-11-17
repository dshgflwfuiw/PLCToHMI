using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace PlcToHmi
{
    /// <summary>
    /// 主窗口交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly PlcConversionService _conversionService = new();
        private string? _lastGeneratedFilePath;

        public MainWindow()
        {
            InitializeComponent();

            if (!System.ComponentModel.DesignerProperties.GetIsInDesignMode(this))
            {
                TryLoadApplicationIcon();
            }
        }

        /// <summary>
        /// 安全加载嵌入式程序图标
        /// </summary>
        private void TryLoadApplicationIcon()
        {
            try
            {
                var stream = Application.GetResourceStream(new Uri("pack://application:,,,/favicon.ico"));
                if (stream?.Stream != null)
                {
                    var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource = stream.Stream;
                    bitmap.EndInit();
                    Icon = bitmap;
                }
            }
            catch
            {
                // 图标加载失败不阻碍程序主功能
            }
        }

        /// <summary>
        /// 格式化并输出日志到文本区域
        /// </summary>
        private void LogMessage(string message, bool isError = false, bool isWarning = false)
        {
            Dispatcher.Invoke(() =>
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss");
                string level = isError ? "ERROR" : (isWarning ? "WARN" : "INFO");
                string logLine = $"{timestamp} - [{level}] {message}{Environment.NewLine}";
                logText.AppendText(logLine);
                logText.ScrollToEnd();
            });
        }

        /// <summary>
        /// 点击按钮选择 CSV 文件并开始转换
        /// </summary>
        private async void btnProcess_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Title = "请选择 PLC 变量 CSV 文件（支持多选）",
                Filter = "PLC 导出 CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
                Multiselect = true,
                RestoreDirectory = true
            };

            if (ofd.ShowDialog() == true && ofd.FileNames.Length > 0)
            {
                await ExecuteConversion(ofd.FileNames);
            }
        }

        /// <summary>
        /// 拖拽释放文件处理
        /// </summary>
        private async void Border_Drop(object sender, DragEventArgs e)
        {
            ResetDropBorderVisual();

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                var csvFiles = files.Where(f => f.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)).ToArray();

                if (csvFiles.Length > 0)
                {
                    await ExecuteConversion(csvFiles);
                }
                else
                {
                    MessageBox.Show("请拖拽有效的 .csv 文件！", "文件格式提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void Border_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                dropAreaBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(52, 152, 219));
                dropAreaBorder.Background = new SolidColorBrush(Color.FromRgb(240, 247, 255));
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void Border_DragLeave(object sender, DragEventArgs e)
        {
            ResetDropBorderVisual();
        }

        private void ResetDropBorderVisual()
        {
            dropAreaBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225));
            dropAreaBorder.Background = new SolidColorBrush(Color.FromRgb(248, 249, 250));
        }

        /// <summary>
        /// 执行核心文件转换调度
        /// </summary>
        private async Task ExecuteConversion(string[] plcCsvFilePaths)
        {
            // 1. 获取所选品牌标识
            string brandKey = "Panasonic";
            if (cmbPlcBrand.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag != null)
            {
                brandKey = selectedItem.Tag.ToString() ?? "Panasonic";
            }

            // 2. 通过工厂解耦创建处理器
            var processor = PlcCsvProcessorFactory.Create(brandKey);
            if (processor == null)
            {
                LogMessage($"未找到品牌 '{brandKey}' 对应的处理器，请检查品牌注册。", isError: true);
                MessageBox.Show("未找到对应的处理器！", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 3. 更新 UI 状态
            btnProcess.IsEnabled = false;
            btnProcess.Content = "⏳ 正在转换中，请稍候...";
            btnOpenFolder.IsEnabled = false;
            btnOpenFile.IsEnabled = false;
            logText.Clear();

            // 初始化进度条与统计仪表盘
            progressContainer.Visibility = Visibility.Visible;
            progressBar.Value = 0;
            lblProgressPercent.Text = "0%";
            lblProgressStatus.Text = "准备启动转换任务...";

            statSourceFiles.Text = plcCsvFilePaths.Length.ToString();
            statTotalRows.Text = "--";
            statDuplicates.Text = "--";
            statWarnings.Text = "--";

            dgPreview.ItemsSource = null;
            tabPreview.Header = "📊 标签预览 (前20条)";

            LogMessage($"[启动任务] 已选择 PLC 品牌: {processor.BrandName}");
            LogMessage($"[源文件列表] 共计 {plcCsvFilePaths.Length} 个文件: {string.Join(", ", plcCsvFilePaths.Select(Path.GetFileName))}");

            // 创建进度通知适配器
            var progressReporter = new Progress<ConversionProgress>(p =>
            {
                progressBar.Value = p.Percentage;
                lblProgressPercent.Text = $"{(int)p.Percentage}%";
                lblProgressStatus.Text = p.StatusMessage;
            });

            // 4. 调用转换调度服务（传入进度汇报委托）
            var result = await _conversionService.ConvertAsync(plcCsvFilePaths, processor, LogMessage, progressReporter);

            // 5. 转换结果响应与 UI 恢复
            btnProcess.IsEnabled = true;
            btnProcess.Content = "选择文件开始转换";
            progressContainer.Visibility = Visibility.Collapsed;

            // 刷新统计仪表盘
            statSourceFiles.Text = result.TotalFiles.ToString();
            statTotalRows.Text = result.TotalRows.ToString();
            statDuplicates.Text = result.DuplicatesResolved.ToString();
            statWarnings.Text = result.WarningsCount.ToString();

            if (result.Success && !string.IsNullOrEmpty(result.OutputFilePath))
            {
                _lastGeneratedFilePath = result.OutputFilePath;
                btnOpenFolder.IsEnabled = true;
                btnOpenFile.IsEnabled = true;

                // 加载数据预览表格并切换到预览选项卡
                if (result.PreviewTags != null && result.PreviewTags.Count > 0)
                {
                    dgPreview.ItemsSource = result.PreviewTags;
                    tabPreview.Header = $"📊 标签预览 ({result.PreviewTags.Count}/{result.TotalRows}条)";
                    lblPreviewTip.Text = $"转换成功！威纶通 HMI 标签数据预览（展示前 {result.PreviewTags.Count} 条 / 共计 {result.TotalRows} 条）：";
                    bottomTabControl.SelectedIndex = 1; // 自动切换至预览卡片
                }

                string summary = $"转换成功！\n\n" +
                                 $"• 源文件数: {result.TotalFiles}\n" +
                                 $"• 生成记录: {result.TotalRows} 条\n" +
                                 $"• 消除重名冲突: {result.DuplicatesResolved} 处\n" +
                                 $"• 预警诊断: {result.WarningsCount} 条\n\n" +
                                 $"输出文件已就绪:\n{result.OutputFilePath}";

                MessageBox.Show(summary, "转换成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                bottomTabControl.SelectedIndex = 0; // 失败时切换回日志查看错误
                MessageBox.Show($"转换未完成或失败：\n{result.Message}\n请查阅下方日志获取诊断详情。", "转换失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// 一键定位输出文件所在文件夹
        /// </summary>
        private void btnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_lastGeneratedFilePath) || !File.Exists(_lastGeneratedFilePath))
            {
                MessageBox.Show("输出文件不存在或尚未生成！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                Process.Start("explorer.exe", $"/select,\"{_lastGeneratedFilePath}\"");
            }
            catch (Exception ex)
            {
                LogMessage($"打开目录失败: {ex.Message}", isError: true);
            }
        }

        /// <summary>
        /// 一键使用系统默认程序（如 Excel）打开生成的 CSV 文件
        /// </summary>
        private void btnOpenFile_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_lastGeneratedFilePath) || !File.Exists(_lastGeneratedFilePath))
            {
                MessageBox.Show("输出文件不存在或尚未生成！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(_lastGeneratedFilePath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                LogMessage($"打开文件失败: {ex.Message}", isError: true);
            }
        }

        /// <summary>
        /// 复制日志内容到剪贴板
        /// </summary>
        private void btnCopyLog_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(logText.Text))
            {
                return;
            }

            try
            {
                Clipboard.SetText(logText.Text);
                btnCopyLog.Content = "已复制!";
                Task.Delay(1500).ContinueWith(_ => Dispatcher.Invoke(() => btnCopyLog.Content = "复制日志"));
            }
            catch (Exception ex)
            {
                LogMessage($"复制到剪贴板失败: {ex.Message}", isError: true);
            }
        }

        /// <summary>
        /// 清空日志内容
        /// </summary>
        private void btnClearLog_Click(object sender, RoutedEventArgs e)
        {
            logText.Clear();
        }
    }
}
