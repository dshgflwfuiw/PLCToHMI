using System;
using System.Collections.Generic;

/// <summary>
/// 松下 PLC CSV 处理器
/// 将松下 FP/KW 系列 PLC 变量文件转换为威纶通 HMI 格式
/// </summary>
public class PanasonicCsvProcessor : PlcCsvProcessorBase
{
    public override string BrandName => "Panasonic FP/KW";

    protected override string AddressPattern => @"([A-Za-z]+)([0-9A-Fa-f]+)";

    protected override Dictionary<string, string> PrefixMapping => new(StringComparer.OrdinalIgnoreCase)
    {
        // 常用位继电器
        { "R", "R" },     // 内部继电器位 (Hex)
        { "X", "X" },     // 输入触点位 (Hex)
        { "Y", "Y" },     // 输出触点位 (Hex)
        { "L", "L_Bit" }, // 链接继电器位 -> L_Bit
        // 字/双字寄存器
        { "WR", "WR" },   // 16位数据寄存器
        { "DWR", "WR" },  // 32位数据寄存器
        { "SV", "SV" },   // 16位数据保持寄存器
        { "DSV", "SV" },  // 32位数据保持寄存器
        { "EV", "EV" },   // 16位数据事件寄存器
        { "DEV", "EV" },  // 32位数据事件寄存器
        { "LD", "LD" },   // 16位数据链接寄存器
        { "DLD", "LD" },  // 32位数据链接寄存器
        { "WX", "WX" },   // 16位数据输入字
        { "DWX", "WX" },  // 32位数据输入字
        { "WY", "WY" },   // 16位数据输出字
        { "DWY", "WY" },  // 32位数据输出字
        { "WL", "WL" },   // 16位数据链接字
        { "DWL", "WL" },  // 32位数据链接字
        { "FL", "FL" },   // 16位数据标志位
        { "DFL", "FL" },  // 32位数据标志位
        { "DT", "DT" },   // 16位数据定时器
        { "DDT", "DT" }   // 32位数据定时器
    };
}
