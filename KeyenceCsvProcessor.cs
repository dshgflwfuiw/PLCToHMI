using System;
using System.Collections.Generic;

/// <summary>
/// 基恩士 Keyence KV 系列 PLC CSV 处理器
/// 将基恩士 KV 系列 PLC 变量文件转换为威纶通 HMI 格式
/// </summary>
public class KeyenceCsvProcessor : PlcCsvProcessorBase
{
    public override string BrandName => "Keyence KV";

    protected override string AddressPattern => @"([A-Za-z]+)(\d+)";

    protected override Dictionary<string, string> PrefixMapping => new(StringComparer.OrdinalIgnoreCase)
    {
        { "X", "X" },      // 输入
        { "Y", "Y" },      // 输出
        { "R", "R" },      // 内部继电器
        { "MR", "MR" },    // 内部辅助继电器 (基恩士最常用)
        { "M", "M" },      // 控制继电器
        { "LR", "LR" },    // 保持锁定继电器
        { "CR", "CR" },    // 控制状态继电器
        { "VB", "VB" },    // 内部辅助位
        { "VM", "VM" },    // 内部辅助位
        { "T", "T" },      // 定时器
        { "TN", "TN" },    // 定时器当前值
        { "C", "C" },      // 计数器
        { "CN", "CN" },    // 计数器当前值
        { "D", "D" },      // 数据存储器
        { "DM", "DM" },    // 数据存储器
        { "EM", "EM" },    // 扩展数据存储器
        { "FM", "FM" },    // 文件数据存储器
    };
}
