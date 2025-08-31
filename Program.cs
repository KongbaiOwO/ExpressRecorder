using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using AForge.Video;
using AForge.Video.DirectShow;
using ZXing;
using System.Diagnostics;
using ZXing.Common;

namespace ExpressRecorder
{
    public partial class MainForm : Form
    {
        private FilterInfoCollection videoDevices;
        private VideoCaptureDevice videoSource;
        private DateTime recordingStartTime;
        private System.Windows.Forms.Timer recordingTimer;
        private System.Windows.Forms.Timer displayTimer;
        private Bitmap lastFrame;
        private readonly object frameLock = new object();
        private bool isRecording = false;
        private PictureBox videoBox;
        private ComboBox cameraComboBox;
        private ComboBox resolutionComboBox;
        private ComboBox fpsComboBox;
        private TextBox expressTextBox;
        private ComboBox companyComboBox;
        private Process ffmpegProcess;
        private Stream ffmpegInputStream;
        private BinaryWriter ffmpegWriter;
        private string selectedCode;
        private string selectedCompany;
        private Result lastBarcodeResult;
        private Button startButton;
        private Button stopButton;
        private TextBox savePathTextBox;
        private string saveDirectory;
        private DateTime lastBarcodeTime = DateTime.MinValue;
        private bool isProcessingBarcode = false;
        private Size originalFrameSize;
        private ComboBox encoderComboBox;
        private Dictionary<string, string> availableEncoders;

        // 快递公司正则表达式规则
        private readonly Dictionary<string, string> expressRules = new Dictionary<string, string>
        {
            {"顺丰", @"^SF\d{13}$"},
            {"中通", @"^78\d{12}$"},
            {"圆通", @"^YT\d{13}$"},
            {"申通", @"^77\d{13}$"},
            {"韵达", @"^[34]\d{14}$"},
            {"京东", @"^JD[0-9A-Z]{11,13}$"},
            {"邮政", @"^9\d{12}$"},
            {"极兔", @"^JT\d{13}$"},
            {"其他", @".*"}  // 默认匹配所有
        };

        public MainForm()
        {
            // 设置默认保存路径为exe文件旁的Videos文件夹
            saveDirectory = Path.Combine(Application.StartupPath, "Videos");
            if (!Directory.Exists(saveDirectory))
            {
                Directory.CreateDirectory(saveDirectory);
            }

            InitializeCustomComponents();
        }

        private void InitializeCustomComponents()
        {
            // 初始化UI组件
            this.Text = "快递单号录制软件";
            this.Size = new Size(1000, 800);
            this.StartPosition = FormStartPosition.CenterScreen;

            // 摄像头选择
            Label cameraLabel = new Label { Text = "选择摄像头:", Location = new Point(20, 20), Size = new Size(100, 20) };
            cameraComboBox = new ComboBox { Location = new Point(120, 20), Size = new Size(200, 20), DropDownStyle = ComboBoxStyle.DropDownList };

            // 分辨率选择
            Label resolutionLabel = new Label { Text = "分辨率:", Location = new Point(20, 50), Size = new Size(100, 20) };
            resolutionComboBox = new ComboBox { Location = new Point(120, 50), Size = new Size(100, 20), DropDownStyle = ComboBoxStyle.DropDownList };

            // 帧率选择
            Label fpsLabel = new Label { Text = "帧率:", Location = new Point(230, 50), Size = new Size(50, 20) };
            fpsComboBox = new ComboBox { Location = new Point(280, 50), Size = new Size(60, 20), DropDownStyle = ComboBoxStyle.DropDownList };

            // 快递单号输入
            Label expressLabel = new Label { Text = "快递单号:", Location = new Point(20, 80), Size = new Size(100, 20) };
            expressTextBox = new TextBox { Location = new Point(120, 80), Size = new Size(150, 20) };

            // 快递公司选择
            Label companyLabel = new Label { Text = "快递公司:", Location = new Point(280, 80), Size = new Size(80, 20) };
            companyComboBox = new ComboBox { Location = new Point(360, 80), Size = new Size(100, 20) };

            // 编码器选择
            Label encoderLabel = new Label { Text = "编码器:", Location = new Point(20, 110), Size = new Size(100, 20) };
            encoderComboBox = new ComboBox { Location = new Point(120, 110), Size = new Size(150, 20), DropDownStyle = ComboBoxStyle.DropDownList };

            // 保存路径选择
            Label savePathLabel = new Label { Text = "保存路径:", Location = new Point(20, 140), Size = new Size(100, 20) };
            savePathTextBox = new TextBox { Location = new Point(120, 140), Size = new Size(300, 20), Text = saveDirectory };
            Button browseButton = new Button { Text = "浏览", Location = new Point(430, 140), Size = new Size(60, 20) };

            // 按钮
            startButton = new Button { Text = "开始录制", Location = new Point(20, 170), Size = new Size(80, 30) };
            stopButton = new Button { Text = "停止录制", Location = new Point(110, 170), Size = new Size(80, 30), Enabled = false };
            Button previewButton = new Button { Text = "开启预览", Location = new Point(200, 170), Size = new Size(80, 30) };
            Button closePreviewButton = new Button { Text = "关闭预览", Location = new Point(290, 170), Size = new Size(80, 30), Enabled = false };

            // 视频显示区域
            videoBox = new PictureBox
            {
                Location = new Point(20, 210),
                Size = new Size(640, 480),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.Black,
                SizeMode = PictureBoxSizeMode.Zoom
            };

            // 添加控件到窗体
            this.Controls.AddRange(new Control[] { cameraLabel, cameraComboBox, resolutionLabel, resolutionComboBox,
                fpsLabel, fpsComboBox, expressLabel, expressTextBox, companyLabel, companyComboBox,
                encoderLabel, encoderComboBox, savePathLabel, savePathTextBox, browseButton,
                startButton, stopButton, previewButton, closePreviewButton, videoBox });

            // 初始化编码器列表
            InitializeEncoders();

            // 浏览按钮事件
            browseButton.Click += (s, e) =>
            {
                using (FolderBrowserDialog folderDialog = new FolderBrowserDialog())
                {
                    folderDialog.SelectedPath = saveDirectory;
                    if (folderDialog.ShowDialog() == DialogResult.OK)
                    {
                        saveDirectory = folderDialog.SelectedPath;
                        savePathTextBox.Text = saveDirectory;
                    }
                }
            };

            // 填充快递公司下拉框
            foreach (var company in expressRules.Keys)
            {
                companyComboBox.Items.Add(company);
            }
            companyComboBox.SelectedIndex = 0;

            // 获取摄像头设备
            videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            foreach (FilterInfo device in videoDevices)
            {
                cameraComboBox.Items.Add(device.Name);
            }

            // 设置摄像头选择事件
            cameraComboBox.SelectedIndexChanged += CameraComboBox_SelectedIndexChanged;

            // 如果有摄像头设备，选择第一个并初始化分辨率
            if (cameraComboBox.Items.Count > 0)
            {
                cameraComboBox.SelectedIndex = 0;
                // 手动调用事件处理程序，初始化分辨率和帧率
                CameraComboBox_SelectedIndexChanged(cameraComboBox, EventArgs.Empty);
            }

            // 预览按钮事件
            previewButton.Click += (s, ev) =>
            {
                if (cameraComboBox.SelectedIndex < 0) return;

                videoSource = new VideoCaptureDevice(videoDevices[cameraComboBox.SelectedIndex].MonikerString);

                // 设置分辨率
                if (resolutionComboBox.SelectedItem != null)
                {
                    string[] res = resolutionComboBox.SelectedItem.ToString().Split('x');
                    var selectedCap = videoSource.VideoCapabilities
                        .FirstOrDefault(c => c.FrameSize.Width == int.Parse(res[0]) && c.FrameSize.Height == int.Parse(res[1]));

                    if (selectedCap != null)
                    {
                        videoSource.VideoResolution = selectedCap;
                        originalFrameSize = selectedCap.FrameSize;
                    }
                }

                videoSource.NewFrame += VideoSource_NewFrame;
                videoSource.Start();
                previewButton.Enabled = false;
                closePreviewButton.Enabled = true;
            };

            // 关闭预览按钮事件
            closePreviewButton.Click += (s, ev) =>
            {
                if (videoSource != null && videoSource.IsRunning)
                {
                    videoSource.SignalToStop();
                    videoSource.WaitForStop();
                    videoBox.Image = null;
                }
                previewButton.Enabled = true;
                closePreviewButton.Enabled = false;
            };

            // 开始录制按钮事件
            startButton.Click += (s, ev) =>
            {
                if (string.IsNullOrEmpty(expressTextBox.Text))
                {
                    MessageBox.Show("请输入快递单号");
                    return;
                }

                StartRecording(expressTextBox.Text, companyComboBox.SelectedItem.ToString());
                startButton.Enabled = false;
                stopButton.Enabled = true;
            };

            // 停止录制按钮事件
            stopButton.Click += (s, ev) =>
            {
                StopRecording();
                startButton.Enabled = true;
                stopButton.Enabled = false;
            };

            // 初始化计时器
            recordingTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            recordingTimer.Tick += RecordingTimer_Tick;

            displayTimer = new System.Windows.Forms.Timer { Interval = 40 };
            displayTimer.Tick += DisplayTimer_Tick;
            displayTimer.Start();
        }

        // 摄像头选择事件处理程序
        private void CameraComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cameraComboBox.SelectedIndex >= 0)
            {
                resolutionComboBox.Items.Clear();
                fpsComboBox.Items.Clear();

                videoSource = new VideoCaptureDevice(videoDevices[cameraComboBox.SelectedIndex].MonikerString);
                foreach (var cap in videoSource.VideoCapabilities)
                {
                    string resolution = $"{cap.FrameSize.Width}x{cap.FrameSize.Height}";
                    if (!resolutionComboBox.Items.Contains(resolution))
                        resolutionComboBox.Items.Add(resolution);

                    string fps = cap.AverageFrameRate.ToString();
                    if (!fpsComboBox.Items.Contains(fps))
                        fpsComboBox.Items.Add(fps);
                }

                if (resolutionComboBox.Items.Count > 0)
                    resolutionComboBox.SelectedIndex = 0;
                if (fpsComboBox.Items.Count > 0)
                    fpsComboBox.SelectedIndex = 0;
            }
        }

        // 初始化编码器列表
        private void InitializeEncoders()
        {
            availableEncoders = new Dictionary<string, string>
            {
                {"libx264 (CPU)", "libx264"}
            };

            // 尝试检测可用的硬件编码器
            try
            {
                // 简化GPU检测，避免使用WMI
                if (File.Exists(Path.Combine(Environment.SystemDirectory, "nvcuda.dll")))
                {
                    availableEncoders.Add("H.264 NVIDIA (NVENC)", "h264_nvenc");
                    availableEncoders.Add("H.265 NVIDIA (NVENC)", "hevc_nvenc");
                }

                // 检查AMD和Intel编码器的简单方法
                // 这里只是示例，实际检测可能需要更复杂的方法
                string programFiles = Environment.GetEnvironmentVariable("ProgramFiles");
                if (Directory.Exists(Path.Combine(programFiles, "AMD")))
                {
                    availableEncoders.Add("H.264 AMD (AMF)", "h264_amf");
                    availableEncoders.Add("H.265 AMD (AMF)", "hevc_amf");
                }

                if (Directory.Exists(Path.Combine(programFiles, "Intel")))
                {
                    availableEncoders.Add("H.264 Intel (QSV)", "h264_qsv");
                    availableEncoders.Add("H.265 Intel (QSV)", "hevc_qsv");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"检测编码器时出错: {ex.Message}");
            }

            // 添加编码器到下拉框
            foreach (var encoder in availableEncoders)
            {
                encoderComboBox.Items.Add(encoder.Key);
            }

            // 默认选择第一个可用的编码器
            if (encoderComboBox.Items.Count > 0)
                encoderComboBox.SelectedIndex = 0;
        }

        private void VideoSource_NewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            try
            {
                // 使用锁来确保线程安全
                lock (frameLock)
                {
                    // 释放旧的帧
                    if (lastFrame != null)
                    {
                        lastFrame.Dispose();
                        lastFrame = null;
                    }

                    // 处理新帧
                    lastFrame = (Bitmap)eventArgs.Frame.Clone();
                }

                // 条形码识别 - 只在未录制时进行
                if (!isRecording && !isProcessingBarcode && (DateTime.Now - lastBarcodeTime).TotalSeconds > 2)
                {
                    // 使用优化的条码识别设置，专注于快递单号格式
                    BarcodeReader reader = new BarcodeReader
                    {
                        Options = new DecodingOptions
                        {
                            PossibleFormats = new List<BarcodeFormat>
                            {
                                BarcodeFormat.CODE_128, // 快递单号最常用的格式
                                BarcodeFormat.CODE_39,
                                BarcodeFormat.QR_CODE
                            },
                            TryHarder = false, // 设置为false以减少误识别
                            PureBarcode = true, // 设置为true以专注于纯条码
                            TryInverted = false // 不尝试识别反转的条码
                        },
                        AutoRotate = false // 禁用自动旋转以减少误识别
                    };

                    Result result = reader.Decode(lastFrame);

                    if (result != null && !string.IsNullOrEmpty(result.Text))
                    {
                        string code = result.Text.Trim();

                        // 初步过滤：只处理看起来像快递单号的条码
                        if (IsLikelyExpressCode(code))
                        {
                            string company = IdentifyExpressCompany(code);

                            // 存储识别结果用于绘制绿框
                            lastBarcodeResult = result;
                            lastBarcodeTime = DateTime.Now;

                            // 只有当识别到非"其他"快递时才弹窗
                            if (company != "其他")
                            {
                                isProcessingBarcode = true;

                                this.Invoke(new Action(() =>
                                {
                                    ExpressDialog dialog = new ExpressDialog(code, company, expressRules.Keys.ToArray());
                                    if (dialog.ShowDialog() == DialogResult.OK)
                                    {
                                        selectedCode = dialog.SelectedCode;
                                        selectedCompany = dialog.SelectedCompany;
                                        StartRecording(selectedCode, selectedCompany);
                                        lastBarcodeResult = null;
                                    }
                                    isProcessingBarcode = false;
                                }));
                            }
                        }
                    }
                    else
                    {
                        lastBarcodeResult = null;
                    }
                }

                // 录制时处理帧
                if (isRecording && ffmpegWriter != null)
                {
                    try
                    {
                        // 创建带水印的帧
                        Bitmap watermarkedFrame = AddWatermarkToFrame((Bitmap)lastFrame.Clone());

                        // 将Bitmap转换为字节数组
                        BitmapData data = watermarkedFrame.LockBits(
                            new Rectangle(0, 0, watermarkedFrame.Width, watermarkedFrame.Height),
                            ImageLockMode.ReadOnly,
                            PixelFormat.Format24bppRgb);

                        byte[] buffer = new byte[data.Stride * data.Height];
                        System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
                        watermarkedFrame.UnlockBits(data);

                        // 写入FFmpeg
                        ffmpegWriter.Write(buffer);

                        watermarkedFrame.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"写入视频帧时出错: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"处理新帧时出错: {ex.Message}");
            }
        }

        // 判断是否可能是快递单号
        private bool IsLikelyExpressCode(string code)
        {
            // 长度检查：快递单号通常在10-20个字符之间
            if (code.Length < 10 || code.Length > 20)
                return false;

            // 内容检查：应该主要是数字和字母
            if (!Regex.IsMatch(code, @"^[A-Za-z0-9]+$"))
                return false;

            // 特定格式检查
            if (Regex.IsMatch(code, @"^SF\d{13}$")) return true; // 顺丰
            if (Regex.IsMatch(code, @"^78\d{12}$")) return true; // 中通
            if (Regex.IsMatch(code, @"^YT\d{13}$")) return true; // 圆通
            if (Regex.IsMatch(code, @"^77\d{13}$")) return true; // 申通
            if (Regex.IsMatch(code, @"^[34]\d{14}$")) return true; // 韵达
            if (Regex.IsMatch(code, @"^JD[0-9A-Z]{11,13}$")) return true; // 京东
            if (Regex.IsMatch(code, @"^9\d{12}$")) return true; // 邮政
            if (Regex.IsMatch(code, @"^JT\d{13}$")) return true; // 极兔

            // 如果不符合特定格式，但长度和字符符合要求，仍然可能是一个有效的快递单号
            return true;
        }

        // 添加水印到帧
        private Bitmap AddWatermarkToFrame(Bitmap frame)
        {
            Bitmap watermarkedFrame = (Bitmap)frame.Clone();
            using (Graphics g = Graphics.FromImage(watermarkedFrame))
            {
                // 添加时间戳和录制时长
                string currentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string recordingTime = isRecording ?
                    (DateTime.Now - recordingStartTime).ToString(@"hh\:mm\:ss") : "00:00:00";

                // 选择字体颜色（根据背景亮度）
                Brush textBrush = GetContrastBrush(watermarkedFrame);

                // 绘制时间信息
                g.DrawString(currentTime, new Font("Arial", 14), textBrush, new PointF(10, 10));
                g.DrawString(recordingTime, new Font("Arial", 14), textBrush, new PointF(10, 40));

                // 如果正在录制，显示录制状态和快递单号
                if (isRecording)
                {
                    g.DrawString("录制中", new Font("Arial", 14), Brushes.Red, new PointF(10, 70));
                    g.DrawString($"{selectedCompany}: {selectedCode}", new Font("Arial", 12), textBrush, new PointF(10, 100));
                }
            }

            return watermarkedFrame;
        }

        private void DisplayTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                Bitmap currentFrame = null;

                // 使用锁来确保线程安全
                lock (frameLock)
                {
                    if (lastFrame != null)
                    {
                        currentFrame = (Bitmap)lastFrame.Clone();
                    }
                }

                if (currentFrame != null)
                {
                    using (currentFrame)
                    {
                        // 缩放图像以适应预览框
                        Bitmap scaledFrame = ScaleImage(currentFrame, videoBox.Width, videoBox.Height);

                        using (Graphics g = Graphics.FromImage(scaledFrame))
                        {
                            // 添加时间戳和录制时长
                            string currentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                            string recordingTime = isRecording ?
                                (DateTime.Now - recordingStartTime).ToString(@"hh\:mm\:ss") : "00:00:00";

                            // 选择字体颜色（根据背景亮度）
                            Brush textBrush = GetContrastBrush(scaledFrame);

                            // 绘制时间信息
                            g.DrawString(currentTime, new Font("Arial", 14), textBrush, new PointF(10, 10));
                            g.DrawString(recordingTime, new Font("Arial", 14), textBrush, new PointF(10, 40));

                            // 如果正在录制，显示录制状态和快递单号
                            if (isRecording)
                            {
                                g.DrawString("录制中", new Font("Arial", 14), Brushes.Red, new PointF(10, 70));
                                g.DrawString($"{selectedCompany}: {selectedCode}", new Font("Arial", 12), textBrush, new PointF(10, 100));
                            }

                            // 绘制识别到的条码框（只在未录制时显示）
                            if (!isRecording && lastBarcodeResult != null)
                            {
                                // 计算缩放比例
                                float scaleX = (float)scaledFrame.Width / currentFrame.Width;
                                float scaleY = (float)scaledFrame.Height / currentFrame.Height;

                                // 绘制绿色矩形框
                                var points = lastBarcodeResult.ResultPoints;
                                if (points != null && points.Length >= 2)
                                {
                                    // 计算矩形框的位置和大小
                                    float minX = float.MaxValue, minY = float.MaxValue;
                                    float maxX = float.MinValue, maxY = float.MinValue;

                                    foreach (var point in points)
                                    {
                                        minX = Math.Min(minX, point.X * scaleX);
                                        minY = Math.Min(minY, point.Y * scaleY);
                                        maxX = Math.Max(maxX, point.X * scaleX);
                                        maxY = Math.Max(maxY, point.Y * scaleY);
                                    }

                                    // 绘制矩形框
                                    using (Pen greenPen = new Pen(Color.LimeGreen, 3))
                                    {
                                        g.DrawRectangle(greenPen, minX, minY, maxX - minX, maxY - minY);
                                    }

                                    // 在框上方显示条码内容
                                    string barcodeText = lastBarcodeResult.Text;
                                    if (barcodeText.Length > 20)
                                    {
                                        barcodeText = barcodeText.Substring(0, 20) + "...";
                                    }

                                    // 绘制背景以便文字更清晰
                                    SizeF textSize = g.MeasureString(barcodeText, new Font("Arial", 12));
                                    g.FillRectangle(Brushes.Black, minX, minY - 25, textSize.Width + 10, textSize.Height + 5);

                                    // 绘制文字
                                    g.DrawString(barcodeText, new Font("Arial", 12), Brushes.LimeGreen, minX + 5, minY - 22);
                                }
                            }
                        }

                        // 更新显示
                        if (videoBox != null)
                        {
                            var oldImage = videoBox.Image;
                            videoBox.Image = scaledFrame;
                            oldImage?.Dispose();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"显示帧时出错: {ex.Message}");
            }
        }

        // 缩放图像以适应指定大小
        private Bitmap ScaleImage(Bitmap image, int maxWidth, int maxHeight)
        {
            var ratioX = (double)maxWidth / image.Width;
            var ratioY = (double)maxHeight / image.Height;
            var ratio = Math.Min(ratioX, ratioY);

            var newWidth = (int)(image.Width * ratio);
            var newHeight = (int)(image.Height * ratio);

            var newImage = new Bitmap(newWidth, newHeight);
            using (Graphics graphics = Graphics.FromImage(newImage))
            {
                graphics.DrawImage(image, 0, 0, newWidth, newHeight);
            }

            return newImage;
        }

        private Brush GetContrastBrush(Bitmap frame)
        {
            // 获取图像右下角区域的平均亮度
            Rectangle sampleArea = new Rectangle(frame.Width - 100, frame.Height - 50, 50, 30);
            double brightness = 0;
            int count = 0;

            for (int x = sampleArea.Left; x < sampleArea.Right && x < frame.Width; x += 2)
            {
                for (int y = sampleArea.Top; y < sampleArea.Bottom && y < frame.Height; y += 2)
                {
                    Color pixel = frame.GetPixel(x, y);
                    brightness += (pixel.R * 0.299 + pixel.G * 0.587 + pixel.B * 0.114) / 255.0;
                    count++;
                }
            }

            if (count > 0)
                brightness /= count;

            // 根据亮度选择黑色或白色文本
            return brightness > 0.5 ? Brushes.Black : Brushes.White;
        }

        private string IdentifyExpressCompany(string code)
        {
            // 首先检查是否是纯数字（可能是快递单号）
            if (Regex.IsMatch(code, @"^\d+$"))
            {
                // 根据长度和前缀判断快递公司
                if (code.Length == 13 && code.StartsWith("SF")) return "顺丰";
                if (code.Length == 14 && code.StartsWith("78")) return "中通";
                if (code.Length == 13 && code.StartsWith("YT")) return "圆通";
                if (code.Length == 15 && code.StartsWith("77")) return "申通";
                if (code.Length == 15 && (code.StartsWith("3") || code.StartsWith("4"))) return "韵达";
                if (code.Length == 15 && code.StartsWith("JD")) return "京东";
                if (code.Length == 13 && code.StartsWith("9")) return "邮政";
                if (code.Length == 13 && code.StartsWith("JT")) return "极兔";
            }

            // 如果不是纯数字，使用正则表达式匹配
            foreach (var rule in expressRules)
            {
                if (Regex.IsMatch(code, rule.Value))
                    return rule.Key;
            }

            return "其他";
        }

        private void StartRecording(string code, string company)
        {
            if (videoSource == null || !videoSource.IsRunning) return;

            // 创建视频文件
            string fileName = $"{DateTime.Now:yyyy-MM-dd}_{DateTime.Now:HH-mm-ss}_{code}_{company}.mp4";
            string fullPath = Path.Combine(saveDirectory, fileName);

            // 使用摄像头相同的分辨率
            Size frameSize = videoSource.VideoResolution.FrameSize;
            int frameRate = videoSource.VideoResolution.AverageFrameRate;

            // 启动FFmpeg进程进行录制
            StartFFmpegRecording(fullPath, frameSize.Width, frameSize.Height, frameRate);

            recordingStartTime = DateTime.Now;
            isRecording = true;
            recordingTimer.Start();

            // 更新按钮状态
            startButton.Enabled = false;
            stopButton.Enabled = true;
        }

        private void StartFFmpegRecording(string fileName, int width, int height, int frameRate)
        {
            try
            {
                // 确保目录存在
                Directory.CreateDirectory(Path.GetDirectoryName(fileName));

                // 获取选择的编码器
                string selectedEncoder = availableEncoders[encoderComboBox.SelectedItem.ToString()];

                // 设置FFmpeg参数 - 使用用户选择的编码器
                string arguments = $"-y -f rawvideo -vcodec rawvideo -pixel_format bgr24 -video_size {width}x{height} " +
                                  $"-framerate {frameRate} -i - -c:v {selectedEncoder} -pix_fmt yuv420p -r {frameRate} ";

                // 添加硬件编码器特定参数
                if (selectedEncoder.Contains("nvenc"))
                {
                    arguments += "-preset p7 -tune hq -b:v 10M -maxrate 20M -bufsize 20M ";
                }
                else if (selectedEncoder.Contains("amf"))
                {
                    arguments += "-quality quality -rc cqp -qp_i 20 -qp_p 20 -qp_b 20 ";
                }
                else if (selectedEncoder.Contains("qsv"))
                {
                    arguments += "-preset veryslow -global_quality 20 ";
                }
                else // libx264
                {
                    arguments += "-preset veryfast -crf 23 ";
                }

                arguments += $"\"{fileName}\"";

                // 启动FFmpeg进程
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardError = true
                };

                ffmpegProcess = new Process { StartInfo = startInfo };
                ffmpegProcess.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Console.WriteLine(e.Data);
                };

                ffmpegProcess.Start();
                ffmpegProcess.BeginErrorReadLine();
                ffmpegInputStream = ffmpegProcess.StandardInput.BaseStream;
                ffmpegWriter = new BinaryWriter(ffmpegInputStream);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法启动FFmpeg: {ex.Message}\n请确保ffmpeg.exe在应用程序目录中。");
                isRecording = false;

                // 恢复按钮状态
                startButton.Enabled = true;
                stopButton.Enabled = false;
            }
        }

        private void StopRecording()
        {
            isRecording = false;
            recordingTimer.Stop();

            // 停止FFmpeg进程
            if (ffmpegWriter != null)
            {
                ffmpegWriter.Close();
                ffmpegWriter = null;
            }

            if (ffmpegProcess != null)
            {
                if (!ffmpegProcess.HasExited)
                {
                    ffmpegProcess.WaitForExit(5000);
                }
                ffmpegProcess.Close();
                ffmpegProcess = null;
            }

            // 恢复按钮状态
            startButton.Enabled = true;
            stopButton.Enabled = false;
        }

        private void RecordingTimer_Tick(object sender, EventArgs e)
        {
            // 这个计时器现在只用于更新UI上的录制时间显示
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            StopRecording();

            if (videoSource != null && videoSource.IsRunning)
            {
                videoSource.SignalToStop();
                videoSource.WaitForStop();
                videoSource = null;
            }

            recordingTimer?.Dispose();
            displayTimer?.Dispose();

            // 使用锁来确保线程安全
            lock (frameLock)
            {
                if (lastFrame != null)
                {
                    lastFrame.Dispose();
                    lastFrame = null;
                }
            }
        }
    }

    // 快递信息确认对话框
    public class ExpressDialog : Form
    {
        public string SelectedCode { get; private set; }
        public string SelectedCompany { get; private set; }

        public ExpressDialog(string code, string company, string[] companies)
        {
            this.Text = "确认快递信息";
            this.Size = new Size(400, 200);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            Label codeLabel = new Label { Text = "快递单号:", Location = new Point(20, 20), Size = new Size(80, 20) };
            TextBox codeTextBox = new TextBox { Text = code, Location = new Point(100, 20), Size = new Size(250, 20) };

            Label companyLabel = new Label { Text = "快递公司:", Location = new Point(20, 50), Size = new Size(80, 20) };
            ComboBox companyComboBox = new ComboBox { Location = new Point(100, 50), Size = new Size(150, 20) };
            companyComboBox.Items.AddRange(companies);
            companyComboBox.SelectedItem = company;

            Button okButton = new Button { Text = "确定", Location = new Point(100, 100), Size = new Size(80, 30), DialogResult = DialogResult.OK };
            Button cancelButton = new Button { Text = "取消", Location = new Point(200, 100), Size = new Size(80, 30), DialogResult = DialogResult.Cancel };

            this.Controls.AddRange(new Control[] { codeLabel, codeTextBox, companyLabel, companyComboBox, okButton, cancelButton });

            this.AcceptButton = okButton;
            this.CancelButton = cancelButton;

            okButton.Click += (s, e) =>
            {
                SelectedCode = codeTextBox.Text;
                SelectedCompany = companyComboBox.SelectedItem?.ToString() ?? "其他";
            };
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}