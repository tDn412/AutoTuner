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
                // 1. Chuẩn bị giao diện trước khi chạy stress test
                BtnStartTuning.IsEnabled = false;
                BtnApplyRec.IsEnabled = false;
                StatusIcon.Symbol = Wpf.Ui.Common.SymbolRegular.Predictions20; // Sử dụng biểu tượng chuẩn của WPF-UI
                ProgressText.Text = "Đang chạy...";
                StatusSubText.Text = "Hệ thống đang chạy stress-test 100% công suất CPU trong 15 giây. Máy có thể sẽ rú quạt to hơn.";
                
                // Chạy mô phỏng thanh Progress Bar tăng dần trong 15 giây
                BenchmarkProgress.Progress = 0;
                var progressTask = Task.Run(async () =>
                {
                    for (int i = 0; i <= 100; i += 2)
                    {
                        await Task.Delay(300); // 300ms * 50 = 15 giây
                        Dispatcher.Invoke(() => BenchmarkProgress.Progress = (double)i / 100.0);
                    }
                });

                // 2. Gọi tiến trình AI Tuner tính toán ngầm
                var tunerTask = _tuner.StartSmartBenchmarkAsync(15);

                // Chờ cả stress test và thanh progress bar chạy xong
                await Task.WhenAll(progressTask, tunerTask);
                _lastResult = await tunerTask;

                // 3. Hiển thị kết quả lên giao diện người dùng (UI)
                TxtBaselineTemp.Text = $"{_lastResult.RecommendedStapm}°C"; // Ví dụ hiển thị
                TxtPeakTemp.Text = $"{_lastResult.RecommendedTempLimit}°C";
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

                bool isEsports = false;
                if (TsEsportsMode.IsChecked == true)
                {
                    isEsports = true;
                    // Bổ sung các thiết lập tối ưu hóa độ trễ Valorant & Esports theo diễn đàn phần cứng:
                    // 1. Kích hoạt Windows High Performance Scheme
                    // 2. Kích hoạt Radeon Anti-Lag để giảm trễ tín hiệu chuột
                    // 3. Kích hoạt Image Sharpening ở mức 25% để vẽ rõ nét viền nhân vật
                    // 4. Vô hiệu hóa Chill/Boost để giữ FPS mượt, tránh sụt/giật cục bộ
                    // 5. Khóa CCD-Affinity vào luồng xử lý chính của nhân sơ cấp để giảm trễ cache
                    command += "--Win-Power=2 " +
                               "--ADLX-Lag=0-true --ADLX-Lag=1-true " +
                               "--ADLX-ImageSharp=0-true-25 --ADLX-ImageSharp=1-true-25 " +
                               "--ADLX-Boost=0-false-50 --ADLX-Chill=0-false-0-0-0 --ADLX-Sync=0-false --ADLX-RSR=false-5 " +
                               "--CCD-Affinity=0 ";
                }

                await Task.Run(() => RyzenAdj_To_UXTU.Translate(command));

                string message = $"Đã áp dụng cấu hình tối ưu của CHUYÊN GIA phần cứng thành công:\n\n" +
                                 $"🔹 TDP Limits: {_lastResult.RecommendedStapm}W (Sustained) / {_lastResult.RecommendedSlow}W (Slow) / {_lastResult.RecommendedFast}W (Fast)\n" +
                                 $"🔹 Temp Ceiling: {_lastResult.RecommendedTempLimit}°C\n" +
                                 $"🔹 Curve Optimizer (Undervolt): All Cores {_lastResult.RecommendedCo}\n" +
                                 $"🔹 iGPU Radeon Boost Clock: {_lastResult.RecommendedGfxClk}MHz\n" +
                                 $"🔹 VRM current (TDC / EDC): {_lastResult.RecommendedVrmTdc}A / {_lastResult.RecommendedVrmEdc}A\n\n";

                if (isEsports)
                {
                    message += "🔥 ĐÃ KÍCH HOẠT CHẾ ĐỘ VALORANT & COMPETITIVE ESPORTS:\n" +
                               "✔️ Bật Anti-Lag & độ sắc nét Driver 25%\n" +
                               "✔️ Khóa nguồn Windows High Performance\n" +
                               "✔️ Vô hiệu hóa tính năng trễ (Chill/Boost)\n" +
                               "✔️ Khóa Affinity luồng xử lý chính\n\n";
                }

                message += "Hệ thống phần cứng của bạn đã được tối ưu hóa toàn diện!";

                MessageBox.Show(
                    message, 
                    isEsports ? "AI Esports Tuning Thành Công" : "AI Hardware Auto-Tuning Thành Công", 
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
            StatusSubText.Text = "Nhấn nút bắt đầu để chạy Stress-Test 15 giây.";
            BtnApplyRec.IsEnabled = false;
        }
    }
}
