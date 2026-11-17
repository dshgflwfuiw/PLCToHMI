using System;
using System.Collections.Generic;

/// <summary>
/// 台达 Delta DVP 系列 PLC CSV 处理器
/// 将台达 DVP 系列 PLC 变量文件转换为威纶通 HMI 格式
/// </summary>
public class DeltaCsvProcessor : PlcCsvProcessorBase
{
    public override string BrandName => "Delta DVP";

    protected override string AddressPattern => @"([A-Za-z]+)(\d+)";

    protected override Dictionary<string, string> PrefixMapping => new(StringComparer.OrdinalIgnoreCase)
    {
        { "X", "X" },      // 输入继电器 (八进制)
        { "Y", "Y" },      // 输出继电器 (八进制)
        { "M", "M" },      // 内部辅助继电器
        { "S", "S" },      // 步进继电器
        { "T", "T" },      // 定时器
        { "C", "C" },      // 计数器
        { "D", "D" },      // 16位数据寄存器
        { "DD", "D" },     // 32位数据寄存器
    };
}
