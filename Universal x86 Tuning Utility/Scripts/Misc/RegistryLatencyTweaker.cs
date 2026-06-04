using Microsoft.Win32;
using System;
using System.Diagnostics;

namespace Universal_x86_Tuning_Utility.Scripts.Misc
{
    public static class RegistryLatencyTweaker
    {
        private const string SystemProfilePath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
        private const string TcpipInterfacesPath = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";

        public static void ApplyLatencyTweaks(bool enable)
        {
            try
            {
                // 1. Cấu hình độ ưu tiên phản hồi hệ thống (System Responsiveness & Network Throttling Index)
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(SystemProfilePath, true))
                {
                    if (key != null)
                    {
                        if (enable)
                        {
                            key.SetValue("SystemResponsiveness", 0, RegistryValueKind.DWord); // 0% CPU dự phòng nền
                            key.SetValue("NetworkThrottlingIndex", unchecked((int)0xffffffff), RegistryValueKind.DWord); // Tắt bóp băng thông mạng khi tải nặng
                            Debug.WriteLine("[RegistryLatencyTweaker] Đã áp dụng System Responsiveness & Network Throttling Registry Tweaks.");
                        }
                        else
                        {
                            key.SetValue("SystemResponsiveness", 20, RegistryValueKind.DWord); // Khôi phục mặc định 20%
                            key.SetValue("NetworkThrottlingIndex", 10, RegistryValueKind.DWord); // Khôi phục mặc định index 10
                            Debug.WriteLine("[RegistryLatencyTweaker] Đã khôi phục cài đặt mặc định của System Responsiveness & Network Throttling.");
                        }
                    }
                }

                // 2. Tinh chỉnh TCP giảm Ping trên mọi card mạng (Tắt thuật toán Nagle)
                using (RegistryKey interfacesKey = Registry.LocalMachine.OpenSubKey(TcpipInterfacesPath, true))
                {
                    if (interfacesKey != null)
                    {
                        foreach (string subkeyName in interfacesKey.GetSubKeyNames())
                        {
                            try
                            {
                                using (RegistryKey interfaceKey = interfacesKey.OpenSubKey(subkeyName, true))
                                {
                                    if (interfaceKey != null)
                                    {
                                        if (enable)
                                        {
                                            interfaceKey.SetValue("TcpAckFrequency", 1, RegistryValueKind.DWord); // Phản hồi gói tin ACK tức thì
                                            interfaceKey.SetValue("TCPNoDelay", 1, RegistryValueKind.DWord); // Tắt bộ đệm gộp gói Nagle
                                        }
                                        else
                                        {
                                            // Xóa khóa để khôi phục mặc định của Windows
                                            interfaceKey.DeleteValue("TcpAckFrequency", false);
                                            interfaceKey.DeleteValue("TCPNoDelay", false);
                                        }
                                    }
                                }
                            }
                            catch { /* Bỏ qua nếu adapter bị khóa hoặc không ghi được */ }
                        }
                        Debug.WriteLine(enable 
                            ? "[RegistryLatencyTweaker] Đã áp dụng TCP Ack/NoDelay cho tất cả card mạng."
                            : "[RegistryLatencyTweaker] Đã khôi phục cài đặt TCP card mạng về mặc định.");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RegistryLatencyTweaker] Lỗi khi ghi registry: {ex.Message}");
            }
        }
    }
}
