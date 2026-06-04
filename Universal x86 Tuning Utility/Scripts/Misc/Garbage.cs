using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Universal_x86_Tuning_Utility.Scripts.Misc
{
    class Garbage
    {
        [DllImport("psapi.dll")]
        static extern int EmptyWorkingSet(IntPtr hwProc);

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern uint NtSetSystemInformation(int systemInformationClass, IntPtr systemInformation, int systemInformationLength);

        private const int SystemMemoryListInformation = 80;
        private const int MemoryPurgeStandbyList = 4;

        public static async Task Garbage_Collect()
        {
            try
            {
                await Task.Run(() =>
                {
                    EmptyWorkingSet(Process.GetCurrentProcess().Handle);
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                });
            }
            catch { }
        }

        public static async Task PurgeSystemMemoryAsync()
        {
            try
            {
                await Task.Run(() =>
                {
                    // 1. Dọn dẹp Working Sets của tất cả các tiến trình nền nhàn rỗi (ngoại trừ UXTU)
                    EmptyAllWorkingSets();

                    // 2. Dọn dẹp Standby List hệ thống để giải phóng bộ nhớ đệm
                    ClearStandbyList();
                    
                    // 3. Gọi Garbage Collect cho bản thân ứng dụng
                    EmptyWorkingSet(Process.GetCurrentProcess().Handle);
                    GC.Collect();
                });
            }
            catch { }
        }

        private static void ClearStandbyList()
        {
            try
            {
                int command = MemoryPurgeStandbyList;
                IntPtr pCommand = Marshal.AllocHGlobal(sizeof(int));
                Marshal.WriteInt32(pCommand, command);
                
                // Gọi ntdll để dọn standby list
                NtSetSystemInformation(SystemMemoryListInformation, pCommand, sizeof(int));
                
                Marshal.FreeHGlobal(pCommand);
            }
            catch { }
        }

        private static void EmptyAllWorkingSets()
        {
            try
            {
                Process currentProc = Process.GetCurrentProcess();
                foreach (Process proc in Process.GetProcesses())
                {
                    try
                    {
                        if (proc.Id == currentProc.Id || proc.Id <= 4) continue;
                        EmptyWorkingSet(proc.Handle);
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
