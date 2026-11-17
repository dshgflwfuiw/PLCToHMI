using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace PlcToHmi
{
    /// <summary>
    /// 内置自动化测试套件
    /// 支持在 CI/CD 或命令行环境中通过 --test 参数快速回归所有核心逻辑
    /// </summary>
    public static class PlcToHmiSelfTest
    {
        public static async Task<int> RunAllTestsAsync()
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("========================================");
            Console.WriteLine(" PLC To HMI 自动化测试与质量验证");
            Console.WriteLine("========================================");

            int passed = 0;
            int failed = 0;

            void Assert(string testName, bool condition, string detail = "")
            {
                if (condition)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write("[PASS] ");
                    Console.ResetColor();
                    Console.WriteLine(testName);
                    passed++;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write("[FAIL] ");
                    Console.ResetColor();
                    Console.WriteLine($"{testName} => {detail}");
                    failed++;
                }
            }

            // 测试 1：松下处理器地址与类型映射
            try
            {
                var panasonic = new PanasonicCsvProcessor();
                string tempCsv = Path.GetTempFileName() + ".csv";
                File.WriteAllText(tempCsv, "序号;变量名称;说明;PLC地址;数据类型\n1;Pump_Run;泵运行;R10;BOOL\n2;Temp_Value;温度值;WR0;INT\n3;Total_Flow;累计流量;DWR10;DINT", Encoding.GetEncoding("GBK"));
                var rows = panasonic.ParsePlcCsvToHmiRows(tempCsv, null);

                Assert("松下处理器：基本行解析", rows != null && rows.Count == 3);
                Assert("松下处理器：R 内部继电器位解析", rows?[0][2] == "R" && rows?[0][3] == "10" && rows?[0][5] == "Bit");
                Assert("松下处理器：WR 16位寄存器解析", rows?[1][2] == "WR" && rows?[1][3] == "0" && rows?[1][5] == "16-bit Signed");
                Assert("松下处理器：DWR 32位映射为 WR 与 32位类型", rows?[2][2] == "WR" && rows?[2][3] == "10" && rows?[2][5] == "32-bit Signed");

                File.Delete(tempCsv);
            }
            catch (Exception ex)
            {
                Assert("松下处理器异常", false, ex.Message);
            }

            // 测试 2：三菱处理器（位、字、双字、Q系列十六进制）
            try
            {
                var mitsubishi = new MitsubishiCsvProcessor();
                string tempCsv = Path.GetTempFileName() + ".csv";
                File.WriteAllText(tempCsv, "No;Name;Comment;Address;Type\n1;Start_Btn;;X0;BOOL\n2;Run_Relay;;M100;BOOL\n3;Speed_Cmd;;D200;INT\n4;Position_Dword;;DD500;DINT\n5;Q_Link_Relay;;B1A;BOOL", Encoding.UTF8);
                var rows = mitsubishi.ParsePlcCsvToHmiRows(tempCsv, null);

                Assert("三菱处理器：有效数据行提取", rows != null && rows.Count == 5);
                Assert("三菱处理器：输入 X0 解析", rows?[0][2] == "X" && rows?[0][3] == "0" && rows?[0][5] == "Bit");
                Assert("三菱处理器：DD 映射为 D 且保持32位", rows?[3][2] == "D" && rows?[3][3] == "500" && rows?[3][5] == "32-bit Signed");
                Assert("三菱处理器：Q系列 B 寄存器十六进制解析", rows?[4][2] == "B" && rows?[4][3] == "1A");

                File.Delete(tempCsv);
            }
            catch (Exception ex)
            {
                Assert("三菱处理器异常", false, ex.Message);
            }

            // 测试 3：西门子处理器（S7-200 SMART V区位/字/双字 与 S7-1200 DB 块）
            try
            {
                var siemens = new SiemensCsvProcessor();
                string tempCsv = Path.GetTempFileName() + ".csv";
                File.WriteAllText(tempCsv, "序号;名称;说明;PLC地址;数据类型\n1;Alarm_Bit;;V100.0;BOOL\n2;Pressure;;VW200;INT\n3;Set_Freq;;VD300;REAL\n4;DB_Dword;;DB1.DBD10;DINT\n5;DB_Bit;;DB2.DBX0.5;BOOL", Encoding.UTF8);
                var rows = siemens.ParsePlcCsvToHmiRows(tempCsv, null);

                Assert("西门子处理器：行解析成功", rows != null && rows.Count == 5);
                Assert("西门子处理器：V 位地址映射为 V_Bit", rows?[0][2] == "V_Bit" && rows?[0][3] == "100.0" && rows?[0][5] == "Bit");
                Assert("西门子处理器：VW 字与类型标准化", rows?[1][2] == "VW" && rows?[1][3] == "200" && rows?[1][5] == "16-bit Signed");
                Assert("西门子处理器：VD 双字与浮点类型", rows?[2][2] == "VD" && rows?[2][3] == "300" && rows?[2][5] == "32-bit Float");
                Assert("西门子处理器：DB1.DBD10 映射为 DB1_DD", rows?[3][2] == "DB1_DD" && rows?[3][3] == "10");
                Assert("西门子处理器：DB2.DBX0.5 映射为 DB2_Bit", rows?[4][2] == "DB2_Bit" && rows?[4][3] == "0.5");

                File.Delete(tempCsv);
            }
            catch (Exception ex)
            {
                Assert("西门子处理器异常", false, ex.Message);
            }

            // 测试 4：汇川处理器（AutoShop 与 InoProShop Codesys %MW 格式）
            try
            {
                var huichuan = new HuichuanCsvProcessor();
                string tempCsv = Path.GetTempFileName() + ".csv";
                File.WriteAllText(tempCsv, "Name;Address;Type\nVar1;D10;INT\nVar2;%MW100;WORD\nVar3;%MX0.1;BOOL", Encoding.UTF8);
                var rows = huichuan.ParsePlcCsvToHmiRows(tempCsv, null);

                Assert("汇川处理器：行解析成功", rows != null && rows.Count == 3);
                Assert("汇川处理器：AutoShop D10 寄存器", rows?[0][2] == "D" && rows?[0][3] == "10");
                Assert("汇川处理器：InoProShop %MW100 Codesys 格式", rows?[1][2] == "MW" && rows?[1][3] == "100" && rows?[1][5] == "16-bit Unsigned");
                Assert("汇川处理器：InoProShop %MX0.1 映射为 M 位", rows?[2][2] == "M" && rows?[2][3] == "0.1");

                File.Delete(tempCsv);
            }
            catch (Exception ex)
            {
                Assert("汇川处理器异常", false, ex.Message);
            }

            // 测试 5：信捷处理器（常规与扩展继电器）
            try
            {
                var xinjie = new XinjieCsvProcessor();
                string tempCsv = Path.GetTempFileName() + ".csv";
                File.WriteAllText(tempCsv, "Name;Address;Type\nNormal_M;M10;BOOL\nExt_Input;X10001;BOOL", Encoding.UTF8);
                var rows = xinjie.ParsePlcCsvToHmiRows(tempCsv, null);

                Assert("信捷处理器：常规 M 继电器", rows?[0][2] == "M" && rows?[0][3] == "10");
                Assert("信捷处理器：地址>=10000 自动映射为 X_Extension", rows?[1][2] == "X_Extension" && rows?[1][3] == "10001");

                File.Delete(tempCsv);
            }
            catch (Exception ex)
            {
                Assert("信捷处理器异常", false, ex.Message);
            }

            // 测试 6：基恩士处理器（MR 辅助继电器与 DM 数据存储器）
            try
            {
                var keyence = new KeyenceCsvProcessor();
                string tempCsv = Path.GetTempFileName() + ".csv";
                File.WriteAllText(tempCsv, "Name;Address;Type\nRelay;MR100;BOOL\nData;DM50;INT", Encoding.UTF8);
                var rows = keyence.ParsePlcCsvToHmiRows(tempCsv, null);

                Assert("基恩士处理器：MR 内部继电器支持", rows?[0][2] == "MR" && rows?[0][3] == "100");
                Assert("基恩士处理器：DM 数据存储器支持", rows?[1][2] == "DM" && rows?[1][3] == "50");

                File.Delete(tempCsv);
            }
            catch (Exception ex)
            {
                Assert("基恩士处理器异常", false, ex.Message);
            }

            // 测试 7：CsvHelper RFC 4180 特殊字符支持（字段内含逗号、分号与转义双引号）
            try
            {
                var mitsubishi = new MitsubishiCsvProcessor();
                string tempCsv = Path.GetTempFileName() + ".csv";
                // 模拟含有逗号、分号及双引号注释的严苛 CSV
                string csvContent = "\"变量名称\";\"说明注释\";\"PLC地址\";\"数据类型\"\n" +
                                   "\"Motor, Main\";\"Line 1; Part \"\"A\"\"\";\"D0\";\"INT\"\n" +
                                   "\"Valve_2\";\"Simple comment\";\"Y0\";\"BOOL\"\n";
                File.WriteAllText(tempCsv, csvContent, Encoding.UTF8);
                var rows = mitsubishi.ParsePlcCsvToHmiRows(tempCsv, null);

                Assert("CsvHelper 解析：含逗号和转义引号变量名不破坏列结构", rows != null && rows.Count == 2);
                Assert("CsvHelper 解析：变量名 'Motor, Main' 准确保留", rows?[0][0] == "Motor, Main");
                Assert("CsvHelper 解析：地址 D0 正确定位", rows?[0][2] == "D" && rows?[0][3] == "0");

                File.Delete(tempCsv);
            }
            catch (Exception ex)
            {
                Assert("CsvHelper 特殊字符测试异常", false, ex.Message);
            }

            // 测试 8：调度服务去重与 32 位双字重叠预警
            try
            {
                var service = new PlcConversionService();
                var processor = new MitsubishiCsvProcessor();
                string temp1 = Path.GetTempFileName() + ".csv";
                string temp2 = Path.GetTempFileName() + ".csv";

                File.WriteAllText(temp1, "Name;Address;Type\nSpeed;D0;DINT\nOther;D1;INT", Encoding.UTF8);
                File.WriteAllText(temp2, "Name;Address;Type\nSpeed;D10;INT", Encoding.UTF8);

                var result = await service.ConvertAsync(new[] { temp1, temp2 }, processor, null);

                Assert("调度服务：多文件合并执行成功", result.Success);
                Assert("调度服务：总记录数正确", result.TotalRows == 3);
                Assert("调度服务：同名变量去重已自动触发", result.DuplicatesResolved == 1);
                Assert("调度服务：32位双字重叠已产生诊断告警", result.WarningsCount >= 1);
                Assert("调度服务：DataGrid 预览数据模型生成成功", result.PreviewTags != null && result.PreviewTags.Count == 3);
                Assert("调度服务：输出文件已实际生成在磁盘", File.Exists(result.OutputFilePath));

                if (File.Exists(result.OutputFilePath)) File.Delete(result.OutputFilePath);
                File.Delete(temp1);
                File.Delete(temp2);
            }
            catch (Exception ex)
            {
                Assert("调度服务测试异常", false, ex.Message);
            }

            // 测试 9：工厂解析
            try
            {
                Assert("工厂模式：支持 7 大品牌注册", PlcCsvProcessorFactory.SupportedBrands.Count == 7);
                Assert("工厂模式：根据 Key 正确创建西门子实例", PlcCsvProcessorFactory.Create("Siemens") is SiemensCsvProcessor);
                Assert("工厂模式：根据 Key 正确创建松下实例", PlcCsvProcessorFactory.Create("Panasonic") is PanasonicCsvProcessor);
            }
            catch (Exception ex)
            {
                Assert("工厂测试异常", false, ex.Message);
            }

            Console.WriteLine("----------------------------------------");
            Console.WriteLine($"测试总结: 共执行 {passed + failed} 项测试 | 通过: {passed} | 失败: {failed}");
            Console.WriteLine("========================================");

            return failed == 0 ? 0 : 1;
        }
    }
}
