using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

/// <summary>
/// 西门子 Siemens S7-200 SMART / S7-1200 系列 PLC CSV 处理器
/// 支持 S7-200 SMART 经典变量区 (V/VW/VD/VB/M/I/Q/SM/T/C) 以及 S7-1200 DB 块与标准绝对地址
/// </summary>
public class SiemensCsvProcessor : PlcCsvProcessorBase
{
    public override string BrandName => "Siemens S7-200/1200";

    protected override string AddressPattern => @"^%?([A-Za-z]+)(\d+(?:\.\d+)?)$";

    protected override Dictionary<string, string> PrefixMapping => new(StringComparer.OrdinalIgnoreCase)
    {
        // V 变量区（S7-200 SMART 最常用）
        { "V", "V_Bit" },    // V 位 -> V_Bit
        { "VB", "VB" },      // V 字节
        { "VW", "VW" },      // V 字
        { "VD", "VD" },      // V 双字
        // M 内部继电器
        { "M", "M" },        // M 位
        { "MB", "MB" },      // M 字节
        { "MW", "MW" },      // M 字
        { "MD", "MD" },      // M 双字
        // I 输入与 Q 输出
        { "I", "I" },        // I 位
        { "IB", "IB" },
        { "IW", "IW" },
        { "ID", "ID" },
        { "Q", "Q" },        // Q 位
        { "QB", "QB" },
        { "QW", "QW" },
        { "QD", "QD" },
        // 特殊与定时器/计数器
        { "SM", "SM" },
        { "SMB", "SMB" },
        { "SMW", "SMW" },
        { "SMD", "SMD" },
        { "T", "T" },
        { "C", "C" },
        { "AIW", "AIW" },
        { "AQW", "AQW" },
    };

    /// <summary>
    /// 解析西门子 PLC 地址，同时支持 DB1.DBD0 与常见 V/M/I/Q 地址
    /// </summary>
    protected override (string symbol, string address) ParseAddress(string plcAddr, string varName, Action<string>? log)
    {
        string trimmed = plcAddr.Trim().TrimStart('%');

        // 1. 匹配 S7-1200/1500 DB 块格式：如 DB1.DBD0, DB2.DBX0.0, DB5.DBW10
        var dbMatch = Regex.Match(trimmed, @"^DB(\d+)\.(DB[XBWDbxwd])(\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase);
        if (dbMatch.Success)
        {
            string dbNumber = dbMatch.Groups[1].Value;
            string dbType = dbMatch.Groups[2].Value.ToUpperInvariant();
            string offset = dbMatch.Groups[3].Value;

            // 威纶通导入 DB 块格式常见为 DBn_DBD, DBn_DBW 或通用 DB
            string symbol = dbType switch
            {
                "DBX" => $"DB{dbNumber}_Bit",
                "DBW" => $"DB{dbNumber}_DW",
                "DBD" => $"DB{dbNumber}_DD",
                _ => $"DB{dbNumber}_{dbType}"
            };
            return (symbol, offset);
        }

        // 2. 匹配 S7-200 SMART 标准变量格式：如 V100.0, VW200, VD300, M0.1
        var stdMatch = Regex.Match(trimmed, @"^([A-Za-z]+)(\d+(?:\.\d+)?)$");
        if (stdMatch.Success)
        {
            string prefix = stdMatch.Groups[1].Value.ToUpperInvariant();
            string address = stdMatch.Groups[2].Value;

            // 如果是以 V 开头且带有点（如 V100.0），确认为位寻址
            if (prefix == "V" && address.Contains('.'))
            {
                return ("V_Bit", address);
            }

            string symbol = PrefixMapping.TryGetValue(prefix, out var mapped) ? mapped : prefix;
            return (symbol, address);
        }

        log?.Invoke($"无法解析西门子变量 '{varName}' 的地址 '{plcAddr}'。");
        return ("UNKNOWN", plcAddr);
    }
}
