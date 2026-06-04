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
            public float BaselineTemp { get; set; }
            public float PeakTemp { get; set; }
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
        public async Task<TunerResult> StartSmartBenchmarkAsync(int durationSeconds = 30, int optimizationType = 0)
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
            return AnalyzeAndTuning(baselineTemp, optimizationType);
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
        private TunerResult AnalyzeAndTuning(float baselineTemp, int optimizationType)
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

            // 1. Phân loại hiệu năng tản nhiệt thực tế
            string tdnGradeName;
            if (peakTemp < 78 && riseRate < 1.0) tdnGradeName = "Excellent";
            else if (peakTemp < 86 && riseRate < 1.8) tdnGradeName = "Good";
            else if (peakTemp < 93 && riseRate < 2.5) tdnGradeName = "Average";
            else tdnGradeName = "Poor";

            // 2. Ma trận tối ưu hóa dựa trên loại game và khả năng tản nhiệt
            if (optimizationType == 0) // Esports Mode (Valorant, LOL, CS2) - Ưu tiên ổn định xung CPU, độ trễ thấp
            {
                if (tdnGradeName == "Excellent")
                {
                    grade = "Excellent (Esports - Cực kỳ mát mẻ, CPU boost cao nhất)";
                    stapm = 40; slow = 48; fast = 54; tempLimit = 90;
                    co = -25; gfxClk = 2000; vrmTdc = 70; vrmEdc = 100;
                }
                else if (tdnGradeName == "Good")
                {
                    grade = "Good (Esports - Tản nhiệt tốt, hạ trễ tín hiệu và tối ưu cache)";
                    stapm = 32; slow = 40; fast = 48; tempLimit = 85;
                    co = -20; gfxClk = 2000; vrmTdc = 60; vrmEdc = 85;
                }
                else if (tdnGradeName == "Average")
                {
                    grade = "Average (Esports - Nhiệt độ trung bình, hạ thế trung bình chống tụt FPS)";
                    stapm = 25; slow = 32; fast = 40; tempLimit = 82;
                    co = -15; gfxClk = 1800; vrmTdc = 50; vrmEdc = 70;
                }
                else
                {
                    grade = "Poor/Throttling (Esports - Cần hạ thế nhẹ và khống chế nhiệt để tránh giật hình)";
                    stapm = 20; slow = 25; fast = 30; tempLimit = 78;
                    co = -10; gfxClk = 1600; vrmTdc = 40; vrmEdc = 55;
                }
            }
            else if (optimizationType == 1) // AAA Heavy Games (Wukong, Cyberpunk) - Tối đa hiệu năng GPU Radeon 680M
            {
                if (tdnGradeName == "Excellent")
                {
                    grade = "Excellent (AAA Heavy - Tối đa điện năng cho GPU và dòng VRM)";
                    stapm = 48; slow = 54; fast = 65; tempLimit = 90;
                    co = -20; gfxClk = 2200; vrmTdc = 75; vrmEdc = 110;
                }
                else if (tdnGradeName == "Good")
                {
                    grade = "Good (AAA Heavy - Phân bổ TDP tối ưu cho iGPU 680M 2200MHz)";
                    stapm = 38; slow = 45; fast = 54; tempLimit = 85;
                    co = -15; gfxClk = 2200; vrmTdc = 65; vrmEdc = 90;
                }
                else if (tdnGradeName == "Average")
                {
                    grade = "Average (AAA Heavy - Giới hạn nhẹ TDP để giữ GPU 2000MHz ổn định)";
                    stapm = 30; slow = 38; fast = 45; tempLimit = 82;
                    co = -10; gfxClk = 2000; vrmTdc = 55; vrmEdc = 75;
                }
                else
                {
                    grade = "Poor/Throttling (AAA Heavy - Giảm nhiệt trần và ép GFX vừa phải tránh sập máy)";
                    stapm = 24; slow = 30; fast = 35; tempLimit = 78;
                    co = -5; gfxClk = 1800; vrmTdc = 45; vrmEdc = 60;
                }
            }
            else // Balanced Mode - Tiết kiệm điện, quạt êm ái dùng hàng ngày
            {
                if (tdnGradeName == "Excellent")
                {
                    grade = "Excellent (Balanced - Êm ái tuyệt đối, hiệu năng cân bằng)";
                    stapm = 35; slow = 42; fast = 48; tempLimit = 80;
                    co = -15; gfxClk = 1800; vrmTdc = 60; vrmEdc = 80;
                }
                else if (tdnGradeName == "Good")
                {
                    grade = "Good (Balanced - Cực kỳ mát mẻ, tiếng ồn cực thấp)";
                    stapm = 28; slow = 35; fast = 42; tempLimit = 80;
                    co = -12; gfxClk = 1800; vrmTdc = 50; vrmEdc = 70;
                }
                else if (tdnGradeName == "Average")
                {
                    grade = "Average (Balanced - Nhiệt độ mát mẻ dưới 78°C)";
                    stapm = 22; slow = 28; fast = 35; tempLimit = 78;
                    co = -10; gfxClk = 1600; vrmTdc = 42; vrmEdc = 60;
                }
                else
                {
                    grade = "Poor/Throttling (Balanced - Tiết kiệm điện tối đa, giảm quạt hú)";
                    stapm = 18; slow = 22; fast = 28; tempLimit = 75;
                    co = -5; gfxClk = 1400; vrmTdc = 35; vrmEdc = 50;
                }
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
                RecommendedVrmEdc = vrmEdc,
                BaselineTemp = baselineTemp,
                PeakTemp = peakTemp
            };
        }
    }
}
