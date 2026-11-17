using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CsvHelper;
using CsvHelper.Configuration;

namespace PlcToHmi
{
    /// <summary>
    /// 标签预览模型，用于在 DataGrid 界面表格中绑定展示
    /// </summary>
    public record HmiTagModel(
        int Index,
        string Name,
        string Brand,
        string Symbol,
        string Address,
        string DataType
    );

    /// <summary>
    /// 转换进度通知数据
    /// </summary>
    public record ConversionProgress(
        int CurrentStep,
        int TotalSteps,
        double Percentage,
        string StatusMessage
    );

    /// <summary>
    /// 转换处理结果摘要
    /// </summary>
    public record ConversionResult(
        bool Success,
        string? OutputFilePath,
        int TotalFiles,
        int TotalRows,
        int DuplicatesResolved,
        int WarningsCount,
        string? Message,
        IReadOnlyList<HmiTagModel>? PreviewTags = null
    );

    /// <summary>
    /// PLC 变量转 HMI 核心业务调度服务
    /// 
    /// 职责：
    /// 1. 协调多文件批处理与结果合并；
    /// 2. 规范化命名输出文件（区分单文件与多文件合并）；
    /// 3. 执行威纶通标签唯一性校验（同名自动加序号重命名，防止 HMI 导入失败）；
    /// 4. 32 位寄存器地址重叠检测（如 D0 双字与 D1 冲突告警）；
    /// 5. 写入最终符合 RFC 4180 标准的 HMI 目标文件。
    /// </summary>
    public class PlcConversionService
    {
        /// <summary>
        /// 异步执行转换
        /// </summary>
        public async Task<ConversionResult> ConvertAsync(
            string[] plcCsvFilePaths,
            PlcCsvProcessorBase processor,
            Action<string, bool, bool>? log,
            IProgress<ConversionProgress>? progress = null)
        {
            if (plcCsvFilePaths == null || plcCsvFilePaths.Length == 0)
            {
                return new ConversionResult(false, null, 0, 0, 0, 0, "未选择任何输入文件。");
            }

            int warningsCount = 0;
            void LogInfo(string msg) => log?.Invoke(msg, false, false);
            void LogWarn(string msg) { warningsCount++; log?.Invoke(msg, false, true); }
            void LogErr(string msg) => log?.Invoke(msg, true, false);

            progress?.Report(new ConversionProgress(0, plcCsvFilePaths.Length, 0, "正在初始化转换配置..."));

            string? inputDirectory = Path.GetDirectoryName(plcCsvFilePaths[0]);
            if (string.IsNullOrEmpty(inputDirectory))
            {
                LogErr("无法解析输入文件的目录路径。");
                return new ConversionResult(false, null, 0, 0, 0, 0, "无法解析文件路径。");
            }

            // 单文件与多文件区分命名：单文件不带 _Merged_
            string firstFileNameNoExt = Path.GetFileNameWithoutExtension(plcCsvFilePaths[0]);
            string brandCleanName = Regex.Replace(processor.BrandName, @"[\s\/\(\)]+", "_").Trim('_');
            string outputFileName = plcCsvFilePaths.Length == 1
                ? $"{firstFileNameNoExt}_{brandCleanName}_HMI变量.csv"
                : $"{firstFileNameNoExt}_Merged_{brandCleanName}_HMI变量.csv";

            string hmiCsvFilePath = Path.Combine(inputDirectory, outputFileName);

            LogInfo($"目标文件路径: {hmiCsvFilePath}");
            LogInfo($"准备转换 {plcCsvFilePaths.Length} 个源文件，所选品牌: {processor.BrandName}...");

            var allDataRows = new List<string[]>();

            // 1. 逐个文件解析提取
            for (int i = 0; i < plcCsvFilePaths.Length; i++)
            {
                var file = plcCsvFilePaths[i];
                string fileName = Path.GetFileName(file);
                double percent = (double)i / plcCsvFilePaths.Length * 70; // 前 70% 进度分配给文件读取解析
                progress?.Report(new ConversionProgress(i + 1, plcCsvFilePaths.Length, percent, $"正在解析: {fileName} ({i + 1}/{plcCsvFilePaths.Length})"));

                LogInfo($"--> 正在解析文件: {fileName}");
                var rows = await Task.Run(() => processor.ParsePlcCsvToHmiRows(file, msg => LogInfo(msg)));

                if (rows != null && rows.Count > 0)
                {
                    allDataRows.AddRange(rows);
                    LogInfo($"    提取到 {rows.Count} 行有效变量数据。");
                }
                else
                {
                    LogWarn($"    文件 '{fileName}' 未包含有效数据或解析失败。");
                }
            }

            if (allDataRows.Count == 0)
            {
                LogErr("未能从所选文件中解析出任何有效变量数据，未生成输出文件。");
                progress?.Report(new ConversionProgress(plcCsvFilePaths.Length, plcCsvFilePaths.Length, 100, "未解析到有效变量数据"));
                return new ConversionResult(false, null, plcCsvFilePaths.Length, 0, 0, warningsCount, "无有效变量数据。");
            }

            // 2. 威纶通同名变量检测与重命名（威纶通 EasyBuilder Pro 严禁重复标签名）
            progress?.Report(new ConversionProgress(plcCsvFilePaths.Length, plcCsvFilePaths.Length, 75, "正在执行标签唯一性校验与去重..."));
            var seenNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int duplicatesResolved = 0;

            foreach (var row in allDataRows)
            {
                string originalName = row[0];
                if (seenNames.TryGetValue(originalName, out int count))
                {
                    count++;
                    seenNames[originalName] = count;
                    string newName = $"{originalName}_{count}";
                    LogWarn($"发现重复变量名 '{originalName}'，已自动重命名为 '{newName}' 以防止 HMI 导入冲突。");
                    row[0] = newName;
                    duplicatesResolved++;
                }
                else
                {
                    seenNames[originalName] = 0;
                }
            }

            // 3. 32位双字寄存器地址重叠预警（如 D0 双字占 D0/D1，检测 D1 是否被使用）
            progress?.Report(new ConversionProgress(plcCsvFilePaths.Length, plcCsvFilePaths.Length, 85, "正在检测双字寄存器地址重叠..."));
            CheckAddressOverlap(allDataRows, LogWarn);

            // 4. 组装威纶通标准表头与数据
            var finalOutputRows = new List<string[]>
            {
                new[] { "名称", "品牌", "起始符号", "起始地址", "", "数据类型" }
            };
            finalOutputRows.AddRange(allDataRows);

            // 5. 提取前 20 条数据作为预览列表
            var previewList = allDataRows.Take(20).Select((r, idx) => new HmiTagModel(
                Index: idx + 1,
                Name: r[0],
                Brand: r[1],
                Symbol: r[2],
                Address: r[3],
                DataType: r[5]
            )).ToList();

            // 6. 写入文件（RFC 4180 标准 + UTF-8 带 BOM）
            progress?.Report(new ConversionProgress(plcCsvFilePaths.Length, plcCsvFilePaths.Length, 92, "正在写入威纶通 CSV 目标文件..."));
            try
            {
                var writeConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = false,
                    ShouldQuote = _ => true
                };

                using (var sw = new StreamWriter(hmiCsvFilePath, false, new UTF8Encoding(true)))
                using (var csvWriter = new CsvWriter(sw, writeConfig))
                {
                    foreach (var row in finalOutputRows)
                    {
                        foreach (var field in row)
                        {
                            csvWriter.WriteField(field);
                        }
                        csvWriter.NextRecord();
                    }
                }

                progress?.Report(new ConversionProgress(plcCsvFilePaths.Length, plcCsvFilePaths.Length, 100, "转换完成！"));

                LogInfo($"转换完成！共生成 {allDataRows.Count} 条 HMI 标签记录。");
                if (duplicatesResolved > 0)
                {
                    LogInfo($"已自动消除 {duplicatesResolved} 个同名标签冲突。");
                }

                return new ConversionResult(
                    true,
                    hmiCsvFilePath,
                    plcCsvFilePaths.Length,
                    allDataRows.Count,
                    duplicatesResolved,
                    warningsCount,
                    null,
                    previewList
                );
            }
            catch (Exception ex)
            {
                LogErr($"写入目标文件失败: {ex.Message}");
                progress?.Report(new ConversionProgress(plcCsvFilePaths.Length, plcCsvFilePaths.Length, 100, $"写入失败: {ex.Message}"));
                return new ConversionResult(false, null, plcCsvFilePaths.Length, allDataRows.Count, duplicatesResolved, warningsCount, ex.Message, previewList);
            }
        }

        /// <summary>
        /// 检查 32 位双字寄存器与后续寄存器的潜在重叠（如 D0 占用 D0 与 D1）
        /// </summary>
        private static void CheckAddressOverlap(List<string[]> rows, Action<string> logWarn)
        {
            var occupiedDwords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows)
            {
                string varName = row[0];
                string symbol = row[2];
                string addr = row[3];
                string dataType = row[5];

                bool is32Bit = dataType.IndexOf("32-bit", StringComparison.OrdinalIgnoreCase) >= 0
                            || dataType.IndexOf("Double", StringComparison.OrdinalIgnoreCase) >= 0;

                string key = $"{symbol}:{addr}";

                // 检查该地址是否已被前序双字占用
                if (occupiedDwords.Contains(key))
                {
                    logWarn($"[地址重叠提示] 变量 '{varName}' 使用的寄存器 '{symbol}{addr}' 正处于前序 32 位双字变量的空间中，请确认地址规划。");
                }

                // 如果是 32 位数值且为纯整数编号寄存器（如 D0, VD100），标记下一个相邻地址
                if (is32Bit && int.TryParse(addr, out int num))
                {
                    int nextNum = symbol.Equals("VD", StringComparison.OrdinalIgnoreCase) ? num + 2 : num + 1;
                    occupiedDwords.Add($"{symbol}:{nextNum}");
                }
            }
        }
    }
}
