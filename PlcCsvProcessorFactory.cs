using System;
using System.Collections.Generic;

namespace PlcToHmi
{
    /// <summary>
    /// 支持的 PLC 品牌元信息
    /// </summary>
    /// <param name="Key">内部唯一键（如 Panasonic）</param>
    /// <param name="DisplayName">界面显示名称</param>
    /// <param name="Description">品牌说明</param>
    /// <param name="CreateProcessor">创建处理器实例的委托</param>
    public record BrandInfo(string Key, string DisplayName, string Description, Func<PlcCsvProcessorBase> CreateProcessor);

    /// <summary>
    /// PLC 处理器工厂（负责品牌注册与实例分发）
    /// 遵循开闭原则（OCP），新增品牌只需在此注册即可。
    /// </summary>
    public static class PlcCsvProcessorFactory
    {
        private static readonly List<BrandInfo> _supportedBrands = new()
        {
            new("Panasonic", "松下 (Panasonic FP/KW)", "支持松下FP/KW系列，十六进制/十进制寄存器", () => new PanasonicCsvProcessor()),
            new("Mitsubishi", "三菱 (Mitsubishi FX/Q)", "支持三菱FX/Q系列常用继电器与寄存器", () => new MitsubishiCsvProcessor()),
            new("Siemens", "西门子 (Siemens S7-200/1200)", "支持S7-200 SMART V区/M/Q/I及S7-1200 DB块", () => new SiemensCsvProcessor()),
            new("Huichuan", "汇川 (Inovance H3U/H5U)", "支持汇川H3U/H5U系列常用寄存器与InoProShop格式", () => new HuichuanCsvProcessor()),
            new("Delta", "台达 (Delta DVP)", "支持台达DVP全系列继电器与D寄存器", () => new DeltaCsvProcessor()),
            new("Keyence", "基恩士 (Keyence KV)", "支持基恩士KV系列继电器与DM数据存储器", () => new KeyenceCsvProcessor()),
            new("Xinjie", "信捷 (Xinjie XD/XC)", "支持信捷XD/XC系列常规及扩展区", () => new XinjieCsvProcessor()),
        };

        /// <summary>
        /// 获取当前所有已支持的 PLC 品牌列表
        /// </summary>
        public static IReadOnlyList<BrandInfo> SupportedBrands => _supportedBrands;

        /// <summary>
        /// 根据品牌唯一键或名称创建对应的处理器实例
        /// </summary>
        public static PlcCsvProcessorBase? Create(string brandKey)
        {
            var brand = _supportedBrands.Find(b => 
                b.Key.Equals(brandKey, StringComparison.OrdinalIgnoreCase) ||
                b.DisplayName.Contains(brandKey, StringComparison.OrdinalIgnoreCase));

            return brand?.CreateProcessor();
        }
    }
}
