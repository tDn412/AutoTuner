using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;

namespace Universal_x86_Tuning_Utility.Services
{
    public class AIBenchmarkTuner
    {
        private readonly Computer _computer;
        private readonly List<ThermalDataPoint> _logs;
        private bool _isBenchmarking;

        public struct ThermalDataPoint
        {
            public DateTime Timestamp { get; set; }
            public float CpuPower { get; set; }
            public float CpuTemp { get; set; }
            public float GpuClock { get; set; }
            public int TimeElapsedSec { get; set; }
        }

        public class TunerResult
        {
            public string ThermalGrade { get; set; } = "";
            public int RecommendedStapm { get; set; }
            public int RecommendedSlow { get; set; }
            public int RecommendedFast { get; set; }
            public int RecommendedTempLimit { get; set; }
            public double ThermalDissipationRate { get; set; } // °C/sec under load
            public int RecommendedCo { get; set; } // Curve Optimizer negative offset, e.g. -15
            public int RecommendedGfxClk { get; set; } // GPU Clock in MHz, e.g. 2000
            public int RecommendedVrmTdc { get; set; } // CPU VRM TDC in A, e.g. 65
            public int RecommendedVrmEdc { get; set; } // CPU VRM EDC in A, e.g. 90
        }

        public AIBenchmarkTuner()
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true
            };
            _logs = new List<ThermalDataPoint>();
            _isBenchmarking = false;
        }

        /// <summary>
        /// Chạy tiến trình stress-test và đo lường xung nhịp/nhiệt độ thời gian thực.
        /// </summary>
        public async Task<TunerResult> StartSmartBenchmarkAsync(int durationSeconds = 15)
        {
            if (_isBenchmarking) throw new InvalidOperationException("Tiến trình benchmark đang chạy!");
            _isBenchmarking = true;
            _logs.Clear();
            
            _computer.Open();
            
            // 1. Đo lường Baseline (Nhiệt độ nghỉ)
            float baselineTemp = ReadAverageCpuTemp();
            Debug.WriteLine($"[AI Tuner] Nhiet do idle ban dau: {baselineTemp}°C");

            // 2. Kích hoạt stress test đa luồng (Stress all CPU threads)
            CancellationTokenSource cts = new CancellationTokenSource();
            Task stressTask = Task.Run(() => RunCpuStress(durationSeconds, cts.Token));

            // 3. Theo dõi cảm biến mỗi 500ms
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < durationSeconds)
            {
                await Task.Delay(500);
                
                float power = ReadCpuPackagePower();
                float temp = ReadAverageCpuTemp();
                float gpuClk = ReadGpuCoreClock();

                _logs.Add(new ThermalDataPoint
                {
                    Timestamp = DateTime.Now,
                    CpuPower = power,
                    CpuTemp = temp,
                    GpuClock = gpuClk,
                    TimeElapsedSec = (int)sw.Elapsed.TotalSeconds
                });

                Debug.WriteLine($"[AI Tuner] Time: {sw.Elapsed.TotalSeconds:F1}s | CPU: {power:F1}W, {temp:F1}°C | GPU: {gpuClk}MHz");
            }

            // Dừng stress test
            cts.Cancel();
            await stressTask;
            sw.Stop();
            _computer.Close();
            _isBenchmarking = false;

            // 4. Phân tích kết quả bằng Thuật toán AI Tối Ưu Hóa Năng Lượng (Tuning Engine)
            return AnalyzeAndTuning(baselineTemp);
        }

        private float ReadAverageCpuTemp()
        {
            float tempSum = 0;
            int count = 0;
            foreach (var hardware in _computer.Hardware)
            {
                hardware.Update();
                foreach (var sensor in hardware.Sensors)
                {
                    if (sensor.SensorType == SensorType.Temperature && sensor.Name.Contains("Core"))
                    {
                        tempSum += sensor.Value ?? 0;
                        count++;
                    }
                }
            }
            return count > 0 ? tempSum / count : 45.0f;
        }

        private float ReadCpuPackagePower()
        {
            foreach (var hardware in _computer.Hardware)
            {
                hardware.Update();
                foreach (var sensor in hardware.Sensors)
                {
                    if (sensor.SensorType == SensorType.Power && sensor.Name == "Package")
                    {
                        return sensor.Value ?? 0;
                    }
                }
            }
            return 15.0f;
        }

        private float ReadGpuCoreClock()
        {
            foreach (var hardware in _computer.Hardware)
            {
                hardware.Update();
                foreach (var sensor in hardware.Sensors)
                {
                    if (sensor.SensorType == SensorType.Clock && sensor.Name == "GPU Core")
                    {
                        return sensor.Value ?? 0;
                    }
                }
            }
            return 400.0f;
        }

        private void RunCpuStress(int durationSeconds, CancellationToken token)
        {
            int threadCount = Environment.ProcessorCount;
            List<Thread> threads = new List<Thread>();

            for (int i = 0; i < threadCount; i++)
            {
                Thread t = new Thread(() =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        // Vòng lặp toán học nặng để ép CPU chạy 100% công suất
                        double x = Math.Sqrt(new Random().NextDouble());
                    }
                });
                t.Priority = ThreadPriority.BelowNormal; // Tránh treo hệ thống
                threads.Add(t);
                t.Start();
            }

            // Chờ hết thời gian stress test
            Thread.Sleep(durationSeconds * 1000);
        }

        /// <summary>
        /// Thuật toán phân tích phản hồi nhiệt độ thời gian thực để tính toán giới hạn TDP.
        /// </summary>
        private TunerResult AnalyzeAndTuning(float baselineTemp)
        {
            if (_logs.Count == 0) return new TunerResult();

            float peakTemp = _logs.Max(x => x.CpuTemp);
            float peakPower = _logs.Max(x => x.CpuPower);
            
            // Tính toán tốc độ tăng nhiệt độ trung bình dưới tải (°C/giây)
            double firstHalfTemp = _logs.Where(x => x.TimeElapsedSec <= 5).Average(x => x.CpuTemp);
            double secondHalfTemp = _logs.Where(x => x.TimeElapsedSec > 10).Average(x => x.CpuTemp);
            double riseRate = (secondHalfTemp - firstHalfTemp) / 10.0;

            string grade;
            int stapm, slow, fast, tempLimit;
            int co, gfxClk, vrmTdc, vrmEdc;

            // Decision Matrix dựa trên đặc tính thoát nhiệt của hệ thống tản nhiệt thực tế
            if (peakTemp < 78 && riseRate < 1.0)
            {
                grade = "Excellent (Tản nhiệt cực tốt - Máy gaming dày hoặc tản nhiệt lỏng)";
                stapm = 45;
                slow = 54;
                fast = 65;
                tempLimit = 90;
                co = -20;
                gfxClk = 2200;
                vrmTdc = 75;
                vrmEdc = 110;
            }
            else if (peakTemp >= 78 && peakTemp < 86 && riseRate < 1.8)
            {
                grade = "Good (Tản nhiệt tốt - Ultrabook cao cấp tản kép)";
                stapm = 35;
                slow = 45;
                fast = 54;
                tempLimit = 85;
                co = -15;
                gfxClk = 2000;
                vrmTdc = 65;
                vrmEdc = 90;
            }
            else if (peakTemp >= 86 && peakTemp < 93 && riseRate < 2.5)
            {
                grade = "Average (Tản nhiệt trung bình - Cần giới hạn để chơi game mượt)";
                stapm = 28;
                slow = 35;
                fast = 45;
                tempLimit = 82;
                co = -10;
                gfxClk = 1800;
                vrmTdc = 55;
                vrmEdc = 75;
            }
            else
            {
                grade = "Poor/Throttling (Tản nhiệt kém - Bị khô keo hoặc bụi bẩn)";
                stapm = 22;
                slow = 28;
                fast = 35;
                tempLimit = 78;
                co = -5;
                gfxClk = 1500;
                vrmTdc = 45;
                vrmEdc = 60;
            }

            return new TunerResult
            {
                ThermalGrade = grade,
                RecommendedStapm = stapm,
                RecommendedSlow = slow,
                RecommendedFast = fast,
                RecommendedTempLimit = tempLimit,
                ThermalDissipationRate = Math.Round(riseRate, 2),
                RecommendedCo = co,
                RecommendedGfxClk = gfxClk,
                RecommendedVrmTdc = vrmTdc,
                RecommendedVrmEdc = vrmEdc
            };
        }
    }
}
