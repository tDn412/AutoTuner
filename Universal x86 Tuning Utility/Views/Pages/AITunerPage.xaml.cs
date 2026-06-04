using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Universal_x86_Tuning_Utility.Services;
using Universal_x86_Tuning_Utility.Scripts;

namespace Universal_x86_Tuning_Utility.Views.Pages
{
    /// <summary>
    /// Interaction logic for AITunerPage.xaml
    /// </summary>
    public partial class AITunerPage : Page
    {
        private readonly AIBenchmarkTuner _tuner;
        private AIBenchmarkTuner.TunerResult _lastResult;

        public AITunerPage()
        {
            InitializeComponent();
            _tuner = new AIBenchmarkTuner();
        }

        private async void BtnStartTuning_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1. Lấy thông số từ ComboBox trên giao diện
                int durationSeconds = 30;
                if (CbxDuration.SelectedIndex == 0) durationSeconds = 15;
                else if (CbxDuration.SelectedIndex == 1) durationSeconds = 30;
                else if (CbxDuration.SelectedIndex == 2) durationSeconds = 60;

                int optimizationType = CbxOptimizationType.SelectedIndex; // 0: Esports, 1: AAA, 2: Balanced

                // Chuẩn bị giao diện trước khi chạy stress test
                BtnStartTuning.IsEnabled = false;
                BtnApplyRec.IsEnabled = false;
                CbxDuration.IsEnabled = false;
                CbxOptimizationType.IsEnabled = false;
                StatusIcon.Symbol = Wpf.Ui.Common.SymbolRegular.Predictions20; // Sử dụng biểu tượng chuẩn của WPF-UI
                ProgressText.Text = "Đang chạy...";
                StatusSubText.Text = $"Hệ thống đang chạy stress-test 100% công suất CPU trong {durationSeconds} giây. Máy có thể sẽ rú quạt to hơn.";
                
                // Chạy mô phỏng thanh Progress Bar tăng dần trong thời gian durationSeconds
                BenchmarkProgress.Progress = 0;
                int stepDelay = (durationSeconds * 1000) / 50; // 50 bước
                var progressTask = Task.Run(async () =>
                {
                    for (int i = 0; i <= 100; i += 2)
                    {
                        await Task.Delay(stepDelay);
                        Dispatcher.Invoke(() => BenchmarkProgress.Progress = (double)i / 100.0);
                    }
                });

                // 2. Gọi tiến trình AI Tuner tính toán ngầm
                var tunerTask = _tuner.StartSmartBenchmarkAsync(durationSeconds, optimizationType);

                // Chờ cả stress test và thanh progress bar chạy xong
                await Task.WhenAll(progressTask, tunerTask);
                _lastResult = await tunerTask;

                // 3. Hiển thị kết quả lên giao diện người dùng (UI)
                TxtBaselineTemp.Text = $"{Math.Round(_lastResult.BaselineTemp, 1)} °C";
                TxtPeakTemp.Text = $"{Math.Round(_lastResult.PeakTemp, 1)} °C";
                TxtRiseRate.Text = $"{_lastResult.ThermalDissipationRate} °C/s";
                TxtThermalGrade.Text = _lastResult.ThermalGrade;

                // Hiển thị cấu hình đề xuất
                TxtRecStapm.Text = $"{_lastResult.RecommendedStapm} W";
                TxtRecSlowFast.Text = $"{_lastResult.RecommendedSlow} W / {_lastResult.RecommendedFast} W";
                TxtRecTempLimit.Text = $"{_lastResult.RecommendedTempLimit} °C";
                TxtRecCo.Text = $"{_lastResult.RecommendedCo} (Negative CO)";
                TxtRecGfxClk.Text = $"{_lastResult.RecommendedGfxClk} MHz";
                TxtRecVrm.Text = $"{_lastResult.RecommendedVrmTdc} A / {_lastResult.RecommendedVrmEdc} A";

                // Đặt lại giao diện hoàn thành
                StatusIcon.Symbol = Wpf.Ui.Common.SymbolRegular.CheckmarkCircle24;
                ProgressText.Text = "Hoàn thành!";
                StatusSubText.Text = "Hệ thống đã phân tích thành công đặc tính nhiệt và đưa ra cấu hình tối ưu.";
                
                BtnApplyRec.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi trong quá trình chạy AI Tuning: {ex.Message}", "Lỗi AI Tuner", MessageBoxButton.OK, MessageBoxImage.Error);
                ResetUIState();
            }
            finally
            {
                BtnStartTuning.IsEnabled = true;
                CbxDuration.IsEnabled = true;
                CbxOptimizationType.IsEnabled = true;
            }
        }

        private async void BtnApplyRec_Click(object sender, RoutedEventArgs e)
        {
            if (_lastResult == null) return;

            try
            {
                // Áp dụng trực tiếp cấu hình tối ưu của CHUYÊN GIA phần cứng vào SMU!
                uint coVal = 0x100000 - (uint)Math.Abs(_lastResult.RecommendedCo);

                // RyzenAdj_To_UXTU.Translate mong đợi các đối số cli chuẩn như UXTU presets
                string command = $"--slow-limit={_lastResult.RecommendedSlow * 1000} " +
                                 $"--fast-limit={_lastResult.RecommendedFast * 1000} " +
                                 $"--stapm-limit={_lastResult.RecommendedStapm * 1000} " +
                                 $"--tctl-temp={_lastResult.RecommendedTempLimit} " +
                                 $"--set-coall={coVal} " +
                                 $"--gfx-clk={_lastResult.RecommendedGfxClk} " +
                                 $"--vrm-current={_lastResult.RecommendedVrmTdc * 1000} " +
                                 $"--vrmmax-current={_lastResult.RecommendedVrmEdc * 1000} ";

                int optType = CbxOptimizationType.SelectedIndex;
                string typeDesc = "";

                if (optType == 0) // Esports Mode (Valorant, LOL, CS2)
                {
                    typeDesc = "Game Esports Competitive (Valorant/LOL)";
                    // Tối ưu độ trễ & trễ cache CPU:
                    // - Khóa Windows High Performance (2)
                    // - Bật Radeon Anti-Lag
                    // - Bật Image Sharpening 25% (Rõ viền nhân vật)
                    // - Tắt các tính năng trễ (Chill/Boost)
                    // - Khóa CCD-Affinity về luồng xử lý nhân sơ cấp
                    command += "--Win-Power=2 " +
                               "--ADLX-Lag=0-true --ADLX-Lag=1-true " +
                               "--ADLX-ImageSharp=0-true-25 --ADLX-ImageSharp=1-true-25 " +
                               "--ADLX-Boost=0-false-50 --ADLX-Chill=0-false-0-0-0 --ADLX-Sync=0-false --ADLX-RSR=false-5 " +
                               "--CCD-Affinity=0 ";
                }
                else if (optType == 1) // AAA Heavy Games (Wukong, Cyberpunk)
                {
                    typeDesc = "Game AAA Đồ họa nặng (Black Myth: Wukong)";
                    // Tối ưu hóa tối đa cho GPU và ổn định khung hình:
                    // - Khóa Windows High Performance (2)
                    // - Bật Radeon Image Sharpening 20% (Hình ảnh AAA nét hơn)
                    // - Bật Enhanced Sync (Chống xé hình cho game AAA mà không tăng input lag)
                    // - Tắt Affinity để phân bổ đều cho game AAA ăn luồng rộng
                    command += "--Win-Power=2 " +
                               "--ADLX-Lag=0-false --ADLX-Lag=1-false " +
                               "--ADLX-ImageSharp=0-true-20 --ADLX-ImageSharp=1-true-20 " +
                               "--ADLX-Boost=0-false-50 --ADLX-Chill=0-false-0-0-0 --ADLX-Sync=0-true --ADLX-RSR=false-5 ";
                }
                else // Balanced Mode
                {
                    typeDesc = "Cân bằng dùng hàng ngày (Tiết kiệm điện & Êm ái)";
                    // - Khóa Windows Balanced (1)
                    // - Tắt tối ưu hóa game để giảm hao pin
                    command += "--Win-Power=1 " +
                               "--ADLX-Lag=0-false --ADLX-Lag=1-false " +
                               "--ADLX-ImageSharp=0-false-10 --ADLX-ImageSharp=1-false-10 " +
                               "--ADLX-Sync=0-false ";
                }

                // 1. Áp dụng cấu hình ngay lập tức vào phần cứng
                await Task.Run(() => RyzenAdj_To_UXTU.Translate(command));

                // 2. Lưu cấu hình vào file cài đặt của ứng dụng để tự động nạp lại khi khởi động lại máy!
                Universal_x86_Tuning_Utility.Properties.Settings.Default.CommandString = command;
                Universal_x86_Tuning_Utility.Properties.Settings.Default.Save();

                string message = $"Đã áp dụng và lưu cấu hình tối ưu của CHUYÊN GIA phần cứng thành công:\n\n" +
                                 $"🎮 Chế độ tối ưu: {typeDesc}\n" +
                                 $"🔹 TDP Limits: {_lastResult.RecommendedStapm}W (Sustained) / {_lastResult.RecommendedSlow}W (Slow) / {_lastResult.RecommendedFast}W (Fast)\n" +
                                 $"🔹 Temp Ceiling: {_lastResult.RecommendedTempLimit}°C\n" +
                                 $"🔹 Curve Optimizer (Undervolt): All Cores {_lastResult.RecommendedCo}\n" +
                                 $"🔹 iGPU Radeon Boost Clock: {_lastResult.RecommendedGfxClk}MHz\n" +
                                 $"🔹 VRM current (TDC / EDC): {_lastResult.RecommendedVrmTdc}A / {_lastResult.RecommendedVrmEdc}A\n\n" +
                                 $"📌 Cấu hình này đã được lưu vào hệ thống của UXTU. Nếu bạn bật chế độ 'Start on System Boot' và 'Reapply on Start' trong Settings, cấu hình này sẽ tự động nạp lại khi bạn khởi động lại máy mà không cần chạy lại Auto-Tuner!";

                MessageBox.Show(
                    message, 
                    "AI Tuning Áp Dụng & Lưu Thành Công", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Information
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Không thể áp dụng cấu hình: {ex.Message}", "Lỗi áp dụng", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ResetUIState()
        {
            BenchmarkProgress.Progress = 0;
            StatusIcon.Symbol = Wpf.Ui.Common.SymbolRegular.Predictions20;
            ProgressText.Text = "Sẵn sàng";
            StatusSubText.Text = "Nhấn nút bắt đầu để chạy Stress-Test.";
            BtnApplyRec.IsEnabled = false;
            CbxDuration.IsEnabled = true;
            CbxOptimizationType.IsEnabled = true;
        }
    }
}
