using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

/// <summary>
/// 信捷 PLC CSV 处理器
/// 支持信捷 XD/XC 系列常规寄存器、H 保持继电器/寄存器以及扩展 I/O 区
/// </summary>
public class XinjieCsvProcessor : PlcCsvProcessorBase
{
    public override string BrandName => "XINJE XD Series";

    protected override string AddressPattern => @"([A-Za-z]+)(\d+)";

    protected override Dictionary<string, string> PrefixMapping => new(StringComparer.OrdinalIgnoreCase)
    {
        { "M", "M" },
        { "D", "D" },
        { "X", "X" },
        { "Y", "Y" },
        { "C", "C" },
        { "CD", "CD" },
        { "T", "T" },
        { "TD", "TD" },
        { "HM", "HM" },
        { "HD", "HD" },
        { "HCD", "HCD" },
        { "HTD", "HTD" },
        { "X_Extension", "X_Extension" },
        { "Y_Extension", "Y_Extension" },
    };

    /// <summary>
    /// 解析信捷 PLC 地址（支持 H 停电保持前缀变体与扩展继电器逻辑，地址 >= 10000 自动映射为扩展区）
    /// </summary>
    protected override (string symbol, string address) ParseAddress(string plcAddr, string varName, Action<string>? log)
    {
        var match = Regex.Match(plcAddr, @"^([MDXY]|H[MDXY]|CD|TD|HCD|HTD)(\d+)$", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            log?.Invoke($"无法解析信捷变量 '{varName}' 的地址 '{plcAddr}'。");
            return ("UNKNOWN", plcAddr);
        }

        var prefix = match.Groups[1].Value.ToUpperInvariant();
        var numberText = match.Groups[2].Value;

        if (int.TryParse(numberText, out int number))
        {
            // 信捷扩展模块 I/O 地址通常从 10000 起编
            if (prefix == "X" && number >= 10000)
            {
                prefix = "X_Extension";
            }
            else if (prefix == "Y" && number >= 10000)
            {
                prefix = "Y_Extension";
            }
        }

        string symbol = PrefixMapping.TryGetValue(prefix, out var mapped) ? mapped : prefix;
        return (symbol, numberText);
    }
}
