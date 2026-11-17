using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

/// <summary>
/// 汇川 Inovance H3U/H5U 系列 PLC CSV 处理器
/// 支持传统 AutoShop 寄存器 (D/M/X/Y) 以及 InoProShop (Codesys IEC 61131-3: %MW, %MD, %MX) 格式
/// </summary>
public class HuichuanCsvProcessor : PlcCsvProcessorBase
{
    public override string BrandName => "Inovance H3U/H5U";

    protected override string AddressPattern => @"([A-Za-z]+)(\d+)";

    protected override Dictionary<string, string> PrefixMapping => new(StringComparer.OrdinalIgnoreCase)
    {
        { "X", "X" },      // 输入
        { "Y", "Y" },      // 输出
        { "M", "M" },      // 内部辅助继电器
        { "S", "S" },      // 状态继电器
        { "T", "T" },      // 定时器
        { "C", "C" },      // 计数器
        { "D", "D" },      // 16位数据寄存器
        { "DD", "D" },     // 32位数据寄存器
        { "SM", "SM" },    // 特殊辅助继电器
        { "SD", "SD" },    // 16位特殊寄存器
        { "DSD", "SD" },   // 32位特殊寄存器
        { "Z", "Z" },      // 16位变址寄存器
        { "DZ", "Z" },     // 32位变址寄存器
        { "R", "R" },      // 锁存寄存器
        { "DR", "R" },     // 32位锁存寄存器
        // Codesys 映射
        { "MW", "MW" },    // 内存字
        { "MD", "MD" },    // 内存双字
        { "MX", "M" },     // 内存位 -> 映射为 M
        { "IX", "X" },     // 输入位 -> 映射为 X
        { "QX", "Y" },     // 输出位 -> 映射为 Y
    };

    /// <summary>
    /// 支持解析 InoProShop 导出的 IEC 61131-3 地址 (如 %MW100, %MD50, %MX0.0)
    /// </summary>
    protected override (string symbol, string address) ParseAddress(string plcAddr, string varName, Action<string>? log)
    {
        // 1. 优先匹配 Codesys 风格地址 (以 % 开头)
        var iecMatch = Regex.Match(plcAddr, @"^%([A-Za-z]+)(\d+(?:\.\d+)?)$");
        if (iecMatch.Success)
        {
            string iecPrefix = iecMatch.Groups[1].Value.ToUpperInvariant();
            string iecAddress = iecMatch.Groups[2].Value;

            string symbol = PrefixMapping.TryGetValue(iecPrefix, out var mapped) ? mapped : iecPrefix;
            return (symbol, iecAddress);
        }

        // 2. 回退到标准基类正则解析 (如 D100, M10, DD20)
        return base.ParseAddress(plcAddr, varName, log);
    }
}
