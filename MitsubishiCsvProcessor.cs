using System;
using System.Collections.Generic;

/// <summary>
/// 三菱 PLC CSV 处理器
/// 支持三菱 FX、Q、L 等主流系列继电器与寄存器映射
/// </summary>
public class MitsubishiCsvProcessor : PlcCsvProcessorBase
{
    public override string BrandName => "Mitsubishi FX/Q";

    protected override string AddressPattern => @"([A-Za-z]+)([0-9A-Fa-f]+)";

    protected override Dictionary<string, string> PrefixMapping => new(StringComparer.OrdinalIgnoreCase)
    {
        { "X", "X" },      // 输入继电器 (八进制/十六进制根据驱动)
        { "Y", "Y" },      // 输出继电器 (八进制/十六进制根据驱动)
        { "M", "M" },      // 内部辅助继电器
        { "L", "L" },      // 锁存继电器
        { "S", "S" },      // 状态继电器
        { "B", "B" },      // 链接继电器 (Q系列，十六进制)
        { "SM", "SM" },    // 特殊辅助继电器
        { "T", "T" },      // 定时器
        { "TS", "TS" },    // 定时器触点
        { "TC", "TC" },    // 定时器线圈
        { "TN", "TN" },    // 定时器当前值
        { "C", "C" },      // 计数器
        { "CS", "CS" },    // 计数器触点
        { "CC", "CC" },    // 计数器线圈
        { "CN", "CN" },    // 计数器当前值
        { "D", "D" },      // 16位数据寄存器
        { "DD", "D" },     // 32位数据寄存器
        { "W", "W" },      // 链接寄存器 (Q系列，十六进制)
        { "SD", "SD" },    // 16位特殊数据寄存器
        { "DSD", "SD" },   // 32位特殊数据寄存器
        { "Z", "Z" },      // 16位变址寄存器
        { "DZ", "Z" },     // 32位变址寄存器
        { "R", "R" },      // 16位文件寄存器
        { "DR", "R" }      // 32位文件寄存器
    };
}
