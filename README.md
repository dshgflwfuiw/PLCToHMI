# PLC变量转HMI工具

一个用于将主流品牌 PLC 的 CSV 变量文件转换为威纶通（Weintek EasyBuilder Pro）HMI 标签通信可用格式的 WPF 桌面应用程序。

---

## 核心特性

- **多品牌工业支持**: 覆盖国内工业现场主流 7 大品牌：
  - 松下（Panasonic FP/KW 系列）
  - 三菱（Mitsubishi FX / Q / L 系列）
  - **西门子（Siemens S7-200 SMART / S7-1200 / S7-1500 DB 块）[新增]**
  - 汇川（Inovance H3U AutoShop / H5U InoProShop Codesys 格式）
  - 基恩士（Keyence KV 系列）
  - 台达（Delta DVP 系列）
  - 信捷（Xinjie XD / XC 系列）
- **工业级标准 CSV 解析**: 底层基于 `CsvHelper`，完全符合 RFC 4180 标准。变量名称或说明中包含逗号、分号、双引号等特殊字符时绝不发生列错位。
- **智能编码与 BOM 探测**: 优先识别 UTF-8 BOM，自动兼容 GB18030、GBK、GB2312、UTF-8、Big5 等中文及多语言编码，杜绝乱码。
- **标准威纶通类型映射**: 自动将 PLC 的原始类型标准化映射为 EasyBuilder Pro 规范数据类型（`Bit`、`16-bit Signed`、`32-bit Float` 等）。
- **标签唯一性防冲突**: 威纶通严禁同名标签。当单文件或多文件合并出现重复变量名时，系统自动添加序号重命名（如 `Var_1`）并提示警告，避免 HMI 导入报错。
- **32位双字空间重叠诊断**: 智能识别 32 位双字（如 D0 双字占 D0/D1 空间），若后续有变量使用冲突地址，日志提供温和诊断预警。
- **便捷交互与可视化体验**: 
  - **标签预览表格（DataGrid）**：转换完成后直接在选项卡中清晰展示前 20 条 HMI 标签（名称、品牌、起始符号、起始地址、数据类型），直观核验无需频繁切换 Excel；
  - **转换统计仪表盘**：直观展示源文件数、有效标签行数、消除重名数、诊断预警数 4 项关键指标徽标；
  - **动态平滑进度条**：支持大文件或多文件合并时的百分比与状态文本动态反馈；
  - 支持文件选择对话框与批量拖拽；
  - 智能命名输出文件（单文件不带 `_Merged_`，多文件自动标识 `_Merged_`）；
  - 转换完成后支持一键【打开输出目录】与【查看生成文件】；
  - 支持日志一键复制与清空。
- **自动化测试保障**: 内置 34 项核心用例测试套件，支持在命令行通过 `--test` 一键无头执行回归测试。

---

## 系统要求

- **操作系统**: Windows 10 / 11
- **运行时**: .NET 9.0（若使用自包含单文件发布版本则无需在电脑上预装运行时）

---

## 使用方法

1. 在下拉框中选择对应的 **PLC 品牌**；
2. 点击 **【选择文件开始转换】** 按钮（或直接将一个或多个 CSV 文件拖拽至虚线框内）；
3. 程序自动完成解析、去重、类型映射并输出到源文件同级目录；
4. 转换成功后，可直接点击 **【打开输出目录】** 或 **【查看生成文件】**。

### 输出文件命名规则
- 单文件转换：`原文件名_品牌名_HMI变量.csv`
- 多文件合并：`第一文件原名_Merged_品牌名_HMI变量.csv`

---

## 支持品牌与寄存器覆盖

| 品牌 | 处理器 | 支持的主要寄存器 / 寻址格式 |
|------|--------|--------------------------|
| 松下 FP/KW | PanasonicCsvProcessor | R、X、Y、WR、DWR、SV、DSV、EV、DEV、LD、DLD、WX、WY、WL、FL、DT、DDT 等 |
| 三菱 FX/Q | MitsubishiCsvProcessor | X、Y、M、L、S、B（Q系列十六进制）、SM、D、DD（32位）、SD、W、Z、R、TS/TC/TN、CS/CC/CN 等 |
| 西门子 S7-200/1200 | SiemensCsvProcessor | V（V_Bit）、VB、VW、VD、M、MB、MW、MD、I、Q、SM、T、C、AIW、AQW，以及 S7-1200 DB 块（DB1.DBD0、DB2.DBX0.0） |
| 汇川 H3U/H5U | HuichuanCsvProcessor | 传统 AutoShop（D/DD/M/X/Y/SD/Z/R）以及 InoProShop Codesys 格式（%MW、%MD、%MX、%IX、%QX） |
| 基恩士 KV | KeyenceCsvProcessor | X、Y、R、MR（内部辅助）、M、LR、CR、VB、VM、T/TN、C/CN、D、DM、EM、FM |
| 台达 DVP | DeltaCsvProcessor | X、Y（八进制）、M、S、T、C、D、DD 等 |
| 信捷 XD/XC | XinjieCsvProcessor | M、D、X、Y、C、CD、T、TD、HM、HD、HCD、HTD，扩展区（X/Y >= 10000 自动映射为 X/Y_Extension） |

---

## 威纶通（EasyBuilder Pro）标准数据类型映射表

| PLC 原始数据类型 | 转换后的威纶通数据类型 | 说明 |
|------------------|----------------------|------|
| `BOOL`, `BIT` | `Bit` | 位触点 / 开关量 |
| `INT`, `INT16`, `SHORT` | `16-bit Signed` | 16 位有符号整型 |
| `UINT`, `UINT16`, `WORD`, `BYTE` | `16-bit Unsigned` | 16 位无符号整型 / 字 |
| `DINT`, `INT32`, `LONG` | `32-bit Signed` | 32 位有符号整型 |
| `UDINT`, `UINT32`, `DWORD` | `32-bit Unsigned` | 32 位无符号整型 / 双字 |
| `REAL`, `FLOAT`, `SINGLE` | `32-bit Float` | 32 位单精度浮点数 |
| `LREAL`, `DOUBLE` | `64-bit Float` | 64 位双精度浮点数 |
| `STRING`, `CHAR` | `String` | ASCII 字符串 |
| `BCD` | `16-bit BCD` / `32-bit BCD` | BCD 编码 |

---

## 架构设计

采用 **模板方法模式 + 简单工厂模式**：

```text
PlcCsvProcessorBase (抽象基类)
  ├── 核心流程：BOM/编码探测 → CsvHelper 标准读取 → 字段提取与类型规范化 → RFC 4180 写入
  ├── 抽象属性：BrandName, PrefixMapping, AddressPattern
  └── 扩展钩子：ParseAddress, IsValidHmiRow, ShouldSkipAddress, NormalizeDataType
  
PlcCsvProcessorFactory (工厂类)
  └── 统一管理 7 大 PLC 品牌注册与元信息，消除 UI 层的分支耦合

PlcConversionService (业务调度服务)
  └── 协调单/多文件处理、标签同名自动去重序号、32位双字重叠安全预警与统计汇总
```

---

## 构建与运行

### 1. 运行 GUI 界面
```bash
dotnet run
```

### 2. 运行自动化测试套件
```bash
dotnet run -- --test
```

### 3. 发布单文件免安装版
```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```
输出位于：`bin/Release/net9.0-windows/win-x64/publish/PlcToHmi.exe`

---

## 许可证

仅供学习与工业内部工程使用。
