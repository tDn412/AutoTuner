using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Universal_x86_Tuning_Utility.Scripts.Adaptive
{
    internal class CPUControl
    {
        private static int MinCurveOptimiser = 0; // CO
        private const int PowerLimitIncrement = 2; // watts
        private const int CurveOptimiserIncrement = 1; // CO

        private static int _newPowerLimit = 999; // watts
        public static int _currentPowerLimit; // watts
        private static int _newCO; // CO
        private static int _lastPowerLimit = 1000; // watts
        private static int _lastCO = 0; // CO
        public static int _lastUsage = 0;

        // Các biến điều khiển PID và EMA
        private static double _smoothedTemp = 0.0;
        private static double _integral = 0;
        private static double _lastError = 0;
        private const double Kp = 1.2;  // Proportional gain
        private const double Ki = 0.02; // Integral gain
        private const double Kd = 0.4;  // Derivative gain

        public static string cpuCommand = "";
        public static string coCommand = "";
        public static async void UpdatePowerLimit(int temperature, int cpuLoad, int MaxPowerLimit, int MinPowerLimit, int MaxTemperature)
        {
            try { 
                // Khởi tạo giới hạn công suất ban đầu nếu chưa có hoặc không hợp lệ
                if (_currentPowerLimit <= 0 || _currentPowerLimit > 1000)
                {
                    _currentPowerLimit = (MinPowerLimit + MaxPowerLimit) / 2;
                }

                // 1. Dùng EMA để làm mịn nhiệt độ đầu vào, tránh nhảy xung ảo do gai nhiệt tức thời
                if (_smoothedTemp <= 0) _smoothedTemp = temperature;
                else _smoothedTemp = (0.7 * _smoothedTemp) + (0.3 * temperature);

                // 2. Thuật toán PID
                double error = MaxTemperature - _smoothedTemp;
                
                // Giới hạn tích phân chống windup
                _integral += error;
                _integral = Math.Max(-100, Math.Min(100, _integral));

                double derivative = error - _lastError;
                _lastError = error;

                // Tín hiệu điều chỉnh TDP từ PID
                double controlOutput = (Kp * error) + (Ki * _integral) + (Kd * derivative);

                int tdpAdjustment = 0;
                
                // Nếu nhiệt độ cao quá trần, hạ TDP tỷ lệ thuận với mức vượt ngưỡng trần
                if (error < 0)
                {
                    tdpAdjustment = (int)Math.Floor(controlOutput) - 1; // Giảm nhanh để bảo vệ hệ thống
                }
                // Nếu nhiệt độ mát mẻ và CPU đang tải nặng, nâng TDP dần lên trần
                else if (cpuLoad > 10 && error > 3)
                {
                    tdpAdjustment = (int)Math.Ceiling(controlOutput * 0.4); 
                    if (tdpAdjustment > 4) tdpAdjustment = 4; // Giới hạn bước tăng tối đa mỗi chu kỳ
                }

                _newPowerLimit = _currentPowerLimit + tdpAdjustment;

                if (_newPowerLimit < MinPowerLimit) _newPowerLimit = MinPowerLimit;
                if (_newPowerLimit > MaxPowerLimit) _newPowerLimit = MaxPowerLimit;

                _currentPowerLimit = _newPowerLimit;

                // Áp dụng giới hạn công suất mới nếu có sự thay đổi so với lần áp dụng trước
                if (_newPowerLimit != _lastPowerLimit)
                {
                    int _TDP = _newPowerLimit;

                    // Detect if AMD CPU or APU
                    if (Family.TYPE == Family.ProcessorType.Amd_Apu)
                    {
                        _TDP = _newPowerLimit * 1000;

                        if (_TDP >= 5000)
                        {
                            // Apply new power and temp limit
                            cpuCommand = $"--tctl-temp={MaxTemperature} --cHTC-temp={MaxTemperature} --apu-skin-temp={MaxTemperature} --stapm-limit={_TDP}  --fast-limit={_TDP} --stapm-time=64 --slow-limit={_TDP} --slow-time=128 --vrm-current=300000 --vrmmax-current=300000 --vrmsoc-current=300000 --vrmsocmax-current=300000 ";
                            // Save new TDP to avoid unnecessary reapplies
                            _lastPowerLimit = _newPowerLimit;
                            iGPUControl._currentPowerLimit = _newPowerLimit;
                        }

                    }

                    else if (Family.TYPE == Family.ProcessorType.Amd_Desktop_Cpu)
                    {
                        _TDP = _newPowerLimit * 1000;

                        // Apply new power and temp limit
                        cpuCommand = $"--tctl-temp={MaxTemperature} --ppt-limit={_TDP} --edc-limit={(int)(_TDP * 1.33)} --tdc-limit={(int)(_TDP * 1.33)} ";
                        _lastPowerLimit = _newPowerLimit;
                    }

                    else if (Family.TYPE == Family.ProcessorType.Intel)
                    {
                        _TDP = _newPowerLimit;
                        // Apply new power and temp limit

                        cpuCommand = $"--intel-pl={_newPowerLimit}";
                        _lastPowerLimit = _newPowerLimit;
                    }
                } 
            } catch { }


            _lastUsage = cpuLoad;
        }

        private static int prevCpuLoad = -1;
        public static void CurveOptimiserLimit(int cpuLoad, int MaxCurveOptimiser)
        {
            try
            {
                int newMaxCO = MaxCurveOptimiser;

                // Change max CO limit based on CPU usage
                if (cpuLoad < 10) newMaxCO = MaxCurveOptimiser;
                else if (cpuLoad >= 10 && cpuLoad < 80) newMaxCO = MaxCurveOptimiser - CurveOptimiserIncrement * 2;
                else if (cpuLoad >= 80) newMaxCO = MaxCurveOptimiser;

                if (_lastCO == 0 && prevCpuLoad <= 0) _lastCO = newMaxCO;
                if (prevCpuLoad < 0) prevCpuLoad = 100;

                // Increase CO if the CPU load is increased by 10
                if (cpuLoad > prevCpuLoad + 10)
                {
                    _newCO = _lastCO + CurveOptimiserIncrement;

                    // Store the current CPU load for the next iteration
                    prevCpuLoad = prevCpuLoad + 10;
                }
                // Decrease CO if the CPU load is decreased by 10
                else if (cpuLoad < prevCpuLoad - 10)
                {
                    _newCO = _lastCO - CurveOptimiserIncrement;

                    // Store the current CPU load for the next iteration
                    prevCpuLoad = prevCpuLoad - 10;
                }

                // Make sure min and max CO is not exceeded
                if (_newCO <= MinCurveOptimiser) _newCO = MinCurveOptimiser;
                if (_newCO >= newMaxCO) _newCO = newMaxCO;

                // Make sure CO is within CO max limit + 5
                if (_newCO > 55) _newCO = 55;

                if (cpuLoad < 5) _newCO = 0;

                if (cpuLoad > 80) _newCO = MaxCurveOptimiser;

                // Apply new CO
                if (_newCO != _lastCO) UpdateCO(_newCO);
            } catch { }
        }

        private static async void UpdateCO(int _newCO)
        {
            // Apply new CO
            if (_newCO > 0) coCommand = $"--set-coall={Convert.ToUInt32(0x100000 - (uint)(_newCO))} ";
            else coCommand = $"--set-coall={0} ";

            // Save new CO to avoid unnecessary reapplies
            _lastCO = _newCO;
        }
    }
}
