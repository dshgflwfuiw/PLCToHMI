using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using CsvHelper;
using CsvHelper.Configuration;

/// <summary>
/// PLC CSV 处理器抽象基类
/// 
/// 负责从各类主流 PLC 导出的变量 CSV 中提取“变量名、PLC地址、数据类型”，
/// 并利用 CsvHelper 转换为符合威纶通（Weintek EasyBuilder Pro）HMI 规范的标签 CSV。
/// 
/// 核心流程（模板方法模式）：
/// 1. 智能编码与 BOM 探测，安全读取源文件（支持 GB18030、GBK、UTF-8 等）；
/// 2. 分隔符探测（支持逗号、分号、制表符）与表头列索引自适应定位；
/// 3. 使用 RFC 4180 标准 CSV 解析，安全支持包含逗号、双引号的字段；
/// 4. 变量地址解析、数据类型规范化映射、非法/数组地址过滤；
/// 5. 格式化输出为威纶通标准 CSV。
/// </summary>
public abstract class PlcCsvProcessorBase
{
    private char _detectedDelimiter = ';';

    /// <summary>
    /// PLC 品牌名称（如 "Panasonic FP/KW"、"Mitsubishi FX/Q"）
    /// </summary>
    public abstract string BrandName { get; }

    /// <summary>
    /// PLC 地址前缀映射字典（忽略大小写）
    /// </summary>
    protected abstract Dictionary<string, string> PrefixMapping { get; }

    /// <summary>
    /// 解析 PLC 地址的正则表达式规则
    /// </summary>
    protected abstract string AddressPattern { get; }

    /// <summary>
    /// 尝试读取源文件时的备选编码列表。
    /// 优先使用中国工控软件常用的 GB18030/GBK，随后尝试 UTF-8 与其他编码。
    /// </summary>
    protected virtual Encoding[] EncodingsToTry => new[]
    {
        // 工控软件在 Windows 中最常用的中文编码
        Encoding.GetEncoding("GB18030"),
        Encoding.GetEncoding("GBK"),
        Encoding.GetEncoding("gb2312"),
        // UTF-8（严格校验与宽松模式）
        new UTF8Encoding(false, throwOnInvalidBytes: true),
        new UTF8Encoding(true),
        Encoding.GetEncoding("big5"),
        Encoding.Default
    };

    /// <summary>
    /// 表头中可能表示变量名称的候选列名
    /// </summary>
    private static readonly string[] NameColumnCandidates =
    {
        "名称", "变量名称", "VARNAME", "VAR_NAME", "NAME", "变量", "TAGNAME", "TAG_NAME"
    };

    /// <summary>
    /// 表头中可能表示地址的候选列名
    /// </summary>
    private static readonly string[] AddressColumnCandidates =
    {
        "地址", "PLC地址", "映射地址", "PLC Address", "Address", "映射", "PLC_ADDR", "ADDR"
    };

    /// <summary>
    /// 表头中可能表示数据类型的候选列名
    /// </summary>
    private static readonly string[] DataTypeColumnCandidates =
    {
        "数据类型", "类型", "Type", "DataType", "DType", "DTYPE", "TYPE_NAME"
    };

    /// <summary>
    /// 将 PLC CSV 文件解析并直接写入目标 HMI CSV 文件。
    /// </summary>
    public bool ConvertPlcCsvToHmiCsv(string plcFilepath, string hmiFilepath, Action<string>? log = null)
    {
        if (!File.Exists(plcFilepath))
        {
            log?.Invoke($"文件 '{plcFilepath}' 不存在。");
            return false;
        }

        var rows = ParsePlcCsvToHmiRows(plcFilepath, log);
        if (rows == null || rows.Count == 0)
        {
            log?.Invoke("未能从源文件中提取到有效数据，未生成输出文件。");
            return false;
        }

        var outputData = new List<string[]>
        {
            new[] { "名称", "品牌", "起始符号", "起始地址", "", "数据类型" }
        };
        outputData.AddRange(rows);

        return WriteOutputFile(hmiFilepath, outputData, log);
    }

    /// <summary>
    /// 解析 PLC CSV 文件为 HMI 数据行集合（不包含表头）。
    /// </summary>
    public virtual List<string[]>? ParsePlcCsvToHmiRows(string plcFilepath, Action<string>? log)
    {
        if (!File.Exists(plcFilepath))
        {
            log?.Invoke($"文件 '{plcFilepath}' 不存在，跳过。");
            return null;
        }

        var rawRecords = TryReadRecordsWithCsvHelper(plcFilepath, log);
        if (rawRecords == null || rawRecords.Count == 0)
        {
            log?.Invoke($"无法以已知编码或分隔符读取文件: {plcFilepath}");
            return null;
        }

        var hmiDataRows = new List<string[]>();
        ProcessRecords(rawRecords, hmiDataRows, log);
        return hmiDataRows;
    }

    /// <summary>
    /// 自动检测 BOM、字符编码与最佳分隔符，使用 CsvHelper 读取全部有效行。
    /// </summary>
    protected virtual List<string[]>? TryReadRecordsWithCsvHelper(string filepath, Action<string>? log)
    {
        var delimiterCandidates = new[] { ';', ',', '\t' };
        var headerKeywords = new[] { "名称", "变量", "地址", "Name", "Address", "Tag" };

        // 1. 先检测文件头部是否存在明确的字节序标记 (BOM)
        var bomEncoding = DetectBom(filepath);
        var encodings = bomEncoding != null
            ? new[] { bomEncoding }
            : EncodingsToTry;

        foreach (var encoding in encodings)
        {
            try
            {
                log?.Invoke($"尝试使用编码 '{encoding.WebName}' 读取源文件。");

                // 使用可共享读权限打开，避免被 Excel 占用导致抛出 IOException
                List<string[]> candidateRecords = null!;
                char candidateDelimiter = ';';
                int bestScore = -1;

                foreach (var delimiter in delimiterCandidates)
                {
                    var records = ReadAllRecordsWithDelimiter(filepath, encoding, delimiter);
                    if (records.Count == 0) continue;

                    // 计算当前分隔符的匹配得分（表头匹配与多列行占比）
                    int score = ScoreRecords(records, headerKeywords);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        candidateDelimiter = delimiter;
                        candidateRecords = records;
                    }
                }

                if (candidateRecords != null && bestScore > 0)
                {
                    _detectedDelimiter = candidateDelimiter;
                    log?.Invoke($"编码 '{encoding.WebName}' 验证成功：最佳分隔符='{candidateDelimiter}'，读取记录数={candidateRecords.Count}。");
                    return candidateRecords;
                }
            }
            catch (DecoderFallbackException)
            {
                // 编码解析字节不匹配，静默尝试下一编码
            }
            catch (Exception ex)
            {
                log?.Invoke($"编码 '{encoding.WebName}' 读取异常: {ex.Message}，继续尝试其他编码...");
            }
        }

        return null;
    }

    /// <summary>
    /// 检测文件的 BOM 头，若存在则精准定位编码，避免繁琐遍历。
    /// </summary>
    private static Encoding? DetectBom(string filepath)
    {
        try
        {
            var bom = new byte[4];
            using var file = new FileStream(filepath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            int read = file.Read(bom, 0, 4);
            if (read >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF) return new UTF8Encoding(true);
            if (read >= 2 && bom[0] == 0xFF && bom[1] == 0xFE) return Encoding.Unicode; // UTF-16LE
            if (read >= 2 && bom[0] == 0xFE && bom[1] == 0xFF) return Encoding.BigEndianUnicode; // UTF-16BE
            if (read >= 4 && bom[0] == 0 && bom[1] == 0 && bom[2] == 0xFE && bom[3] == 0xFF) return Encoding.UTF32;
        }
        catch
        {
            // 忽略文件预读 BOM 探测失败，走常规尝试逻辑
        }
        return null;
    }

    /// <summary>
    /// 使用 CsvHelper 底层 CsvParser 读取符合 RFC 4180 标准的数据记录
    /// </summary>
    private static List<string[]> ReadAllRecordsWithDelimiter(string filepath, Encoding encoding, char delimiter)
    {
        var result = new List<string[]>();
        const int maxAttempts = 3;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var fs = new FileStream(filepath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs, encoding);

                var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    Delimiter = delimiter.ToString(),
                    BadDataFound = null,
                    Mode = CsvMode.RFC4180,
                    IgnoreBlankLines = true
                };

                using var parser = new CsvParser(reader, config);
                while (parser.Read())
                {
                    if (parser.Record != null && parser.Record.Length > 0)
                    {
                        result.Add(parser.Record.Select(f => f?.Trim() ?? string.Empty).ToArray());
                    }
                }
                break;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                Thread.Sleep(150);
            }
        }

        return result;
    }

    /// <summary>
    /// 评估提取结果对于目标 PLC 变量文件的合理性得分
    /// </summary>
    private static int ScoreRecords(List<string[]> records, string[] headerKeywords)
    {
        if (records.Count == 0) return -1;

        int score = 0;
        int checkLimit = Math.Min(records.Count, 15);

        for (int i = 0; i < checkLimit; i++)
        {
            var row = records[i];
            if (row.Length < 2) continue;

            // 表头行命中关键词权重极高
            bool hasHeaderMatch = row.Any(cell => headerKeywords.Any(k => cell.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0));
            if (hasHeaderMatch) score += 20;

            // 具有典型变量列数（通常 >= 4 列）
            if (row.Length >= 4) score += 2;
        }

        return score;
    }

    /// <summary>
    /// 处理已解析的 CSV 记录，定位表头并逐行转换
    /// </summary>
    protected virtual void ProcessRecords(List<string[]> rawRecords, List<string[]> hmiDataRows, Action<string>? log)
    {
        int headerIndex = FindHeaderIndex(rawRecords, out var headerColumns, log);

        if (headerIndex < 0 || headerColumns == null)
        {
            log?.Invoke("未识别到标准表头，采用通用默认列位（第2列名称，第4列地址，第5列类型）。");
            headerColumns = rawRecords[0];
            headerIndex = 0;
        }

        int nameIndex = FindColumnIndex(headerColumns, NameColumnCandidates);
        int addressIndex = FindColumnIndex(headerColumns, AddressColumnCandidates);
        int dataTypeIndex = FindColumnIndex(headerColumns, DataTypeColumnCandidates);

        if (nameIndex < 0) nameIndex = 1;
        if (addressIndex < 0) addressIndex = 3;
        if (dataTypeIndex < 0) dataTypeIndex = 4;

        int rowCount = 0;
        for (int i = headerIndex + 1; i < rawRecords.Count; i++)
        {
            rowCount++;
            var record = rawRecords[i];
            if (record.Length == 0 || record.All(string.IsNullOrWhiteSpace)) continue;

            if (IsSectionHeaderLine(record)) continue;

            var row = ProcessSingleRecord(record, rowCount, nameIndex, addressIndex, dataTypeIndex, log);
            if (row != null)
            {
                hmiDataRows.Add(row);
            }
        }
    }

    /// <summary>
    /// 过滤非变量数据区域标记行（如 TYPENAME、VARNUMBER 等分区标识）
    /// </summary>
    private static bool IsSectionHeaderLine(string[] record)
    {
        if (record.Length == 0) return false;
        var firstCell = record[0].Trim();
        return firstCell.Equals("TYPENAME", StringComparison.OrdinalIgnoreCase)
            || firstCell.Equals("VARNUMBER", StringComparison.OrdinalIgnoreCase)
            || firstCell.Equals("SECTION", StringComparison.OrdinalIgnoreCase)
            || firstCell.Equals("ST_TYPE", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 在前若干条记录中自动定位包含名称与地址信息的表头行
    /// </summary>
    private static int FindHeaderIndex(List<string[]> records, out string[]? headerColumns, Action<string>? log)
    {
        headerColumns = null;
        int maxScan = Math.Min(records.Count, 20);

        for (int i = 0; i < maxScan; i++)
        {
            var row = records[i];
            int nameIndex = FindColumnIndex(row, NameColumnCandidates);
            int addressIndex = FindColumnIndex(row, AddressColumnCandidates);

            if (nameIndex >= 0 && addressIndex >= 0)
            {
                headerColumns = row;
                if (i > 0)
                {
                    log?.Invoke($"在第 {i + 1} 行检测到表头，列索引: 名称={nameIndex}, 地址={addressIndex}。");
                }
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// 在列数组中定位候选列名（优先精准匹配，其次子串包含）
    /// </summary>
    private static int FindColumnIndex(string[] columns, string[] candidates)
    {
        for (int i = 0; i < columns.Length; i++)
        {
            if (candidates.Any(c => columns[i].Equals(c, StringComparison.OrdinalIgnoreCase)))
                return i;
        }

        for (int i = 0; i < columns.Length; i++)
        {
            if (candidates.Any(c => columns[i].IndexOf(c, StringComparison.OrdinalIgnoreCase) >= 0))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// 处理单条记录并构造标准威纶通 HMI 6列格式：{ 名称, 品牌, 起始符号, 起始地址, "", 数据类型 }
    /// </summary>
    protected virtual string[]? ProcessSingleRecord(string[] record, int rowCount, int nameIndex, int addressIndex, int dataTypeIndex, Action<string>? log)
    {
        int requiredLen = Math.Max(nameIndex, Math.Max(addressIndex, dataTypeIndex));
        if (record.Length <= requiredLen)
        {
            log?.Invoke($"跳过第 {rowCount} 行：列数不足（需至少 {requiredLen + 1} 列，实际 {record.Length} 列）。");
            return null;
        }

        string varName = record[nameIndex].Trim();
        string plcAddr = record[addressIndex].Trim();
        string rawDataType = dataTypeIndex < record.Length ? record[dataTypeIndex].Trim() : string.Empty;

        if (string.IsNullOrWhiteSpace(plcAddr)) return null;

        if (ShouldSkipAddress(plcAddr, varName, log)) return null;

        if (!string.IsNullOrEmpty(rawDataType) && rawDataType.IndexOf("ARRAY", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            log?.Invoke($"跳过变量 '{varName}'：数据类型 '{rawDataType}' 包含不支持的数组格式。");
            return null;
        }

        var (hmiSymbol, hmiAddress) = ParseAddress(plcAddr, varName, log);

        // 统一校验行合法性，避免向子类暴露重复的判空与 UNKNOWN 处理
        if (!IsValidHmiRow(hmiSymbol, hmiAddress, varName, log))
        {
            return null;
        }

        string hmiDataType = NormalizeDataType(rawDataType);

        return new[] { varName, BrandName, hmiSymbol, hmiAddress, "", hmiDataType };
    }

    /// <summary>
    /// 校验解析生成的 HMI 行是否有效。默认过滤空符号或 UNKNOWN，子类可扩展。
    /// </summary>
    protected virtual bool IsValidHmiRow(string symbol, string address, string varName, Action<string>? log)
    {
        if (string.IsNullOrWhiteSpace(symbol) || symbol.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase))
        {
            log?.Invoke($"跳过变量 '{varName}'：无法将地址 '{address}' 映射为有效的 HMI 符号。");
            return false;
        }
        return true;
    }

    /// <summary>
    /// 判断是否跳过特定地址格式（如数组索引、范围地址等）
    /// </summary>
    protected virtual bool ShouldSkipAddress(string plcAddr, string varName, Action<string>? log)
    {
        if (plcAddr.IndexOf('[') >= 0 || plcAddr.IndexOf(']') >= 0 || plcAddr.IndexOf(',') >= 0)
        {
            log?.Invoke($"跳过变量 '{varName}' 的地址 '{plcAddr}'：不支持连续范围或多维数组。");
            return true;
        }

        if (plcAddr.IndexOf("ARRAY", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            log?.Invoke($"跳过变量 '{varName}' 的地址 '{plcAddr}'：包含关键字 'ARRAY'。");
            return true;
        }

        return false;
    }

    /// <summary>
    /// 根据正则表达式和前缀映射表将 PLC 地址拆解为 HMI 起始符号与起始地址
    /// </summary>
    protected virtual (string symbol, string address) ParseAddress(string plcAddr, string varName, Action<string>? log)
    {
        Match match = Regex.Match(plcAddr, AddressPattern);
        if (match.Success)
        {
            string originalPrefix = match.Groups[1].Value.ToUpperInvariant();
            string address = match.Groups[2].Value;

            string symbol = PrefixMapping.TryGetValue(originalPrefix, out var mapped)
                ? mapped
                : originalPrefix;

            return (symbol, address);
        }

        log?.Invoke($"无法匹配变量 '{varName}' 的地址格式: '{plcAddr}'。");
        return ("UNKNOWN", plcAddr);
    }

    /// <summary>
    /// 将 PLC 原始数据类型标准化映射为威纶通（EasyBuilder Pro）规范标签类型
    /// </summary>
    protected virtual string NormalizeDataType(string rawType)
    {
        if (string.IsNullOrWhiteSpace(rawType)) return "16-bit Unsigned";

        string upper = rawType.Trim().ToUpperInvariant();

        // 1. 位 / 布尔
        if (upper == "BOOL" || upper == "BIT" || upper.Contains("BOOL"))
            return "Bit";

        // 2. 浮点数
        if (upper == "REAL" || upper == "FLOAT" || upper == "SINGLE")
            return "32-bit Float";
        if (upper == "LREAL" || upper == "DOUBLE")
            return "64-bit Float";

        // 3. 32 位整型 / 双字
        if (upper == "DINT" || upper == "INT32" || upper == "LONG")
            return "32-bit Signed";
        if (upper == "UDINT" || upper == "UINT32" || upper == "DWORD" || upper == "ULONG")
            return "32-bit Unsigned";

        // 4. 16 位整型 / 字
        if (upper == "INT" || upper == "INT16" || upper == "SHORT")
            return "16-bit Signed";
        if (upper == "UINT" || upper == "UINT16" || upper == "WORD" || upper == "USHORT")
            return "16-bit Unsigned";

        // 5. 8 位字节
        if (upper == "BYTE" || upper == "USINT" || upper == "UINT8")
            return "16-bit Unsigned";
        if (upper == "SINT" || upper == "INT8")
            return "16-bit Signed";

        // 6. 字符串与其它
        if (upper.StartsWith("STRING") || upper == "CHAR")
            return "String";
        if (upper.Contains("BCD"))
            return upper.Contains("32") ? "32-bit BCD" : "16-bit BCD";
        if (upper == "TIME" || upper == "DATE")
            return "32-bit Unsigned";

        // 如果已是标准形式或无需转换，直接保留
        return rawType.Trim();
    }

    /// <summary>
    /// 使用 CsvHelper 写入符合 RFC 4180 标准的目标 CSV 文件（带 UTF-8 BOM，确保 Excel / EBPro 无乱码）
    /// </summary>
    protected virtual bool WriteOutputFile(string filepath, List<string[]> dataRows, Action<string>? log)
    {
        try
        {
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = false,
                ShouldQuote = _ => true // 威纶通推荐所有字段包裹双引号以确保安全导入
            };

            using var sw = new StreamWriter(filepath, false, new UTF8Encoding(true));
            using var csvWriter = new CsvWriter(sw, config);

            foreach (var row in dataRows)
            {
                foreach (var field in row)
                {
                    csvWriter.WriteField(field);
                }
                csvWriter.NextRecord();
            }

            log?.Invoke($"目标文件已成功写入: '{filepath}'");
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"写入目标文件时发生错误: {ex.Message}");
            return false;
        }
    }
}
