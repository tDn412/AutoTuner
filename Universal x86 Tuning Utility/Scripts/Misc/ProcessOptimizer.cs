using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Universal_x86_Tuning_Utility.Scripts.Misc
{
    public static class ProcessOptimizer
    {
        public static void OptimizeProcess(Process process, bool isEsportsMode)
        {
            try
            {
                if (process == null || process.HasExited) return;

                // 1. Đặt mức ưu tiên CPU tối đa là High cho tiến trình game
                if (process.PriorityClass != ProcessPriorityClass.High)
                {
                    process.PriorityClass = ProcessPriorityClass.High;
                    Debug.WriteLine($"[ProcessOptimizer] Đã đặt độ ưu tiên High cho: {process.ProcessName}");
                }

                // 2. Ghim luồng xử lý vào các Nhân Vật Lý (Physical Cores) để tránh trễ SMT
                if (isEsportsMode)
                {
                    int logicalCount = Environment.ProcessorCount;
                    
                    // Ghim vào các luồng số chẵn (0, 2, 4, 6...) vốn là nhân thực trên các CPU AMD Ryzen 7 6800H (8C/16T)
                    if (logicalCount > 1)
                    {
                        ulong mask = 0;
                        for (int i = 0; i < logicalCount; i += 2)
                        {
                            mask |= (1UL << i);
                        }
                        process.ProcessorAffinity = (IntPtr)mask;
                        Debug.WriteLine($"[ProcessOptimizer] Đã ghim nhân vật lý cho {process.ProcessName} (Mask: {mask:X})");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProcessOptimizer] Lỗi khi tối ưu hóa tiến trình: {ex.Message}");
            }
        }
    }
}
