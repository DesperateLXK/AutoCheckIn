using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System.Runtime.InteropServices;
using Tesseract;
using System.Threading;
using System.Threading.Tasks;
using PaddleOCRSharp;
using MathNet;
namespace AutoCheckIn
{
    public partial class Form1 : Form
    {
        #region 使用WinApi获取正确的分辨率
        [DllImport("User32.dll", EntryPoint = "GetDC")]
        private extern static IntPtr GetDC(IntPtr hWnd);


        [DllImport("gdi32.dll")]
        public static extern int GetDeviceCaps(IntPtr hdc, int nIndex);


        const int DESKTOPVERTRES = 117;
        const int DESKTOPHORZRES = 118;
        #endregion

        // 设置此窗体为活动窗体：
        // 将创建指定窗口的线程带到前台并激活该窗口。键盘输入直接指向窗口，并为用户更改各种视觉提示。
        // 系统为创建前台窗口的线程分配的优先级略高于其他线程。
        [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        // 设置此窗体为活动窗体：
        // 激活窗口。窗口必须附加到调用线程的消息队列。
        [DllImport("user32.dll", EntryPoint = "SetActiveWindow")]
        public static extern IntPtr SetActiveWindow(IntPtr hWnd);

        // 设置窗体位置
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int SetWindowPos(IntPtr hWnd, int hWndInsertAfter, int x, int y, int Width, int Height, int flags);

        string picFilename = "test.png";
        Mat mat;
        Bitmap bitmap;
        #region 内存回收
        [DllImport("kernel32.dll", EntryPoint = "SetProcessWorkingSetSize")]
        public static extern int SetProcessWorkingSetSize(IntPtr process, int minSize, int maxSize);
        /// <summary>
        /// 释放内存
        /// </summary>
        public static void ClearMemory()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                SetProcessWorkingSetSize(System.Diagnostics.Process.GetCurrentProcess().Handle, -1, -1);
            }
        }
        #endregion
        public Form1()
        {

            InitializeComponent();
            Control.CheckForIllegalCrossThreadCalls = false;

        }
        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(value); //隐藏主框体
        }
        private void timer1_Tick(object sender, EventArgs e)
        {

        }
        int SH;
        int SW;
        public void cutScreen()
        {
            Rectangle rect = new Rectangle();
            rect = System.Windows.Forms.Screen.GetWorkingArea(this);
            //richTextBox1.AppendText(rect.ToString());
            System.Drawing.Size mySize = new System.Drawing.Size(rect.Width, rect.Height);
            bitmap = new Bitmap(rect.Width, rect.Height);
            Graphics g = Graphics.FromImage(bitmap);
            g.CopyFromScreen(0, 0, 0, 0, mySize);
            bitmap.Save(picFilename);
            mat = new Mat(picFilename);
            pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;
            // pictureBox1.Image = mat.ToBitmap();
            //释放资源
            bitmap.Dispose();
            g.Dispose();
            GC.Collect();
        }

        private void cutScreenButton_Click(object sender, EventArgs e)
        {
            cutScreen();
        }
        public List<OpenCvSharp.Rect> findTextRegion(Mat dilation)
        {
            List<OpenCvSharp.Rect> region = new List<OpenCvSharp.Rect>();
            // 1. 查找轮廓
            OpenCvSharp.Point[][] contours;
            HierarchyIndex[] hierarchly;
            OpenCvSharp.Rect biggestContourRect = new OpenCvSharp.Rect();

            Cv2.FindContours(dilation, out contours, out hierarchly, RetrievalModes.Tree, ContourApproximationModes.ApproxSimple);

            // 2. 筛选那些面积小的
            foreach (OpenCvSharp.Point[] contour in contours)
            {
                double area = Cv2.ContourArea(contour);

                //面积小的都筛选掉
                if (area < 4000)
                {
                    continue;
                }

                //轮廓近似，作用很小
                double epsilon = 0.001 * Cv2.ArcLength(contour, true);

                //找到最小的矩形
                biggestContourRect = Cv2.BoundingRect(contour);

                if (biggestContourRect.Height > (biggestContourRect.Width * 1.2))
                {
                    continue;
                }
                region.Add(biggestContourRect);
                //画线
                mat.Rectangle(biggestContourRect, new Scalar(0, 255, 0), 2);
            }
            pictureBox1.Image = mat.ToBitmap();
            return region;
            //Cv2.ImShow("img", mat);
        }


        public Mat preprocess(string imgPath)
        {
            Mat dilation2 = new Mat();

            //读取灰度图
            using (Mat src = new Mat(imgPath, ImreadModes.Grayscale))
            {
                //1.Sobel算子，x方向求梯度
                Mat sobel = new Mat();
                Cv2.Sobel(src, sobel, MatType.CV_8U, 1, 0, 3);

                //2.二值化
                Mat binary = new Mat();
                Cv2.Threshold(sobel, binary, 0, 255, ThresholdTypes.Otsu | ThresholdTypes.Binary);

                //3. 膨胀和腐蚀操作的核函数
                Mat element1 = new Mat();
                Mat element2 = new Mat();
                OpenCvSharp.Size size1 = new OpenCvSharp.Size(30, 9);
                OpenCvSharp.Size size2 = new OpenCvSharp.Size(24, 6);

                element1 = Cv2.GetStructuringElement(MorphShapes.Rect, size1);
                element2 = Cv2.GetStructuringElement(MorphShapes.Rect, size2);

                //4. 膨胀一次，让轮廓突出
                Mat dilation = new Mat();
                Cv2.Dilate(binary, dilation, element2);

                //5. 腐蚀一次，去掉细节，如表格线等。注意这里去掉的是竖直的线
                Mat erosion = new Mat();
                Cv2.Erode(dilation, erosion, element1);

                //6. 再次膨胀，让轮廓明显一些
                Cv2.Dilate(erosion, dilation2, element2, null, 3);
            }
            return dilation2;
        }
        public struct img_pak {
            //public  Mat img;
            public  string imgFilename;
            public OpenCvSharp.Rect r;
        };

        public List<img_pak> IMG_PAK = new List<img_pak>();
        //public img_pak imgpacktemp = new img_pak();
        //public List<Mat> IMG_Save = new List<Mat>();
        bool posFlag = false;
        public OpenCvSharp.Rect CutTextImgAndReturnCheckPos(List<OpenCvSharp.Rect>  region_temp)
        {

            //var pos = new List<OpenCvSharp.Point>();
            var pos = new OpenCvSharp.Rect();
            var img = Cv2.ImRead(picFilename);
            //img = img.CvtColor(ColorConversionCodes.BGR2GRAY);
            //img = img.Threshold(0, 255, ThresholdTypes.Otsu | ThresholdTypes.Binary);
            var i = 0;    
            foreach (var item in region_temp)
            {
                Mat newimage = new Mat();
                newimage = img[item.Y > 0 ? item.Y : 0, item.Y + item.Height, item.X > 0 ? item.X : 0, item.X + item.Width];
                var file_name = i.ToString() + ".jpg";
                //Cv2.Resize(newimage, img_gray, size, 4, 4, InterpolationFlags.Nearest);
                Cv2.ImWrite(file_name, newimage);
                string text = GetCheckInName(file_name, 1);
                i++;
                WriteLog(text + "\n", "log");
                if (text.Contains("打") && text.Contains("卡"))
                {
                    richTextBox1.AppendText(text + "\n");
                    richTextBox1.AppendText("(" + item.X.ToString() + "," + item.Y.ToString() + ")\n");
                    richTextBox1.AppendText("(" + item.Width.ToString() + "," + item.Height.ToString() + ")\n");
                    var poss = new OpenCvSharp.Rect(item.X, item.Y, item.Width, item.Height);
                    posFlag = true;
                    ClearMemory();
                    return poss;
                }
                ClearMemory();
                newimage.Dispose();
                GC.Collect();
            }
            return pos;
        }
        
        public string GetCheckInName(string imagePath)
        {
            string img_text = string.Empty;
            using (var ocr = new TesseractEngine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata"), "chi_sim", EngineMode.Default))
            {
                var pix = PixConverter.ToPix(new Bitmap(imagePath));
                
                using (var page = ocr.Process(pix))
                {
                    img_text = page.GetText();//识别后的内容
                    page.Dispose();
                }
                
                pix.Dispose();
            }
            GC.Collect();
            return img_text;
        }

        public string GetCheckInName(string imagePath, int mod)
        {
            string img_text = string.Empty;
            Bitmap bitmap = new Bitmap(imagePath);
            OCRModelConfig config = null;
            OCRParameter oCRParameter = new OCRParameter();
            oCRParameter.numThread = 6;
            oCRParameter.Enable_mkldnn = 1;
            oCRParameter.use_angle_cls = 1;
            oCRParameter.use_polygon_score = 1;
            oCRParameter.BoxScoreThresh = 0.1f;
            OCRResult ocrResult = new OCRResult();

            using (PaddleOCREngine engine = new PaddleOCREngine(config, oCRParameter))
            {

                ocrResult = engine.DetectText(bitmap);

            }
            img_text = ocrResult.Text;
            bitmap.Dispose();
            GC.Collect();
            return img_text;
        }

        [System.Runtime.InteropServices.DllImport("user32")]
        static extern void mouse_event(MouseEventFlag flags, int dx, int dy, uint data, UIntPtr extraInfo);
        [DllImport("User32")]
        public extern static void SetCursorPos(int x, int y);
        [Flags]
        enum MouseEventFlag : uint
        {
            Move = 0x0001,
            LeftDown = 0x0002,
            LeftUp = 0x0004,
            RightDown = 0x0008,
            RightUp = 0x0010,
            MiddleDown = 0x0020,
            MiddleUp = 0x0040,
            XDown = 0x0080,
            XUp = 0x0100,
            Wheel = 0x0800,
            VirtualDesk = 0x4000,
            Absolute = 0x8000
        }
        public static void CMouseClick()
        {
            mouse_event(MouseEventFlag.LeftDown, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MouseEventFlag.LeftUp, 0, 0, 0, UIntPtr.Zero);
        }
        private void checkButton_Click(object sender, EventArgs e)
        {
            
            Mat test = preprocess(picFilename);
            var regin = findTextRegion(test);
            var pos = CutTextImgAndReturnCheckPos(regin);
            int i = 0;
            while (pos.Y + 3 * i < SH)
            {
                SetCursorPos((2 * pos.X + pos.Width) / 2, pos.Y + 4 * i);
                CMouseClick();
                i++;
            }
            
        }

        public int ClearTextImg()
        {
            try
            {
                IMG_PAK.Clear();
                foreach (string d in Directory.GetFileSystemEntries(Application.StartupPath))
                {
                    if (File.Exists(d))
                    {
                        string jpgName = Path.GetFileName(d);
                        if (jpgName.Contains("jpg"))
                        {
                            File.Delete(d);
                        }
                    }
                }
                pictureBox1.Image = null;
                richTextBox1.Clear();
                GC.Collect();
                return 1;
            }
            catch
            {
                return 0;
            }

        }

        private void clearImgButton_Click(object sender, EventArgs e)
        {
            ClearTextImg();
        }


        private void button2_Click(object sender, EventArgs e)
        {
            richTextBox1.AppendText("成功按下\n");
            //MessageBox.Show("打卡成功 !", "Success");
        }
        private void GetMousePoint()
        {
            System.Drawing.Point ms = Control.MousePosition;
            richTextBox2.AppendText("(" +ms.X.ToString()+ "," +ms.Y.ToString() +")\n");
        }
        private void timer1_Tick_1(object sender, EventArgs e)
        {
            GetMousePoint();
            richTextBox2.Focus();
            //设置光标的位置到文本尾   
            richTextBox2.Select(richTextBox2.TextLength, 0);
            //滚动到控件光标处   
            richTextBox2.ScrollToCaret();

        }

 


        private void beginButton_Click(object sender, EventArgs e)
        {
            ClearTextImg();
            //threadStartFlag = true;
            backgroundWorker1.RunWorkerAsync();
            //checkInTimer.Enabled = true;

        }
        private void stopButton_Click(object sender, EventArgs e)
        {
            
            backgroundWorker1.CancelAsync();
            //ClearTextImg();
        }
        private void Form1_Load(object sender, EventArgs e)
        {
            // 设置窗体显示在最上层
            SetWindowPos(this.Handle, -1, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0080);

            // 设置本窗体为活动窗体
            SetActiveWindow(this.Handle);
            SetForegroundWindow(this.Handle);

            // 设置窗体置顶
            this.TopMost = true;
            System.Drawing.Rectangle rec = Screen.GetWorkingArea(this);
            SH = rec.Height;
            SW = rec.Width;

        }

        private void backgroundWorker1_DoWork(object sender, DoWorkEventArgs e)
        {
            //threadStart();
            BackgroundWorker worker = sender as BackgroundWorker;
            while (worker.CancellationPending == false)
            {
                cutScreen();
                Mat test = preprocess(picFilename);
                var regin = findTextRegion(test);
                var pos = CutTextImgAndReturnCheckPos(regin);
                int i = 0;
                if (posFlag == true)
                {
                    while (pos.Y + 4 * i < SH - 200)
                    {
                        SetCursorPos((2 * pos.X + pos.Width) / 2, pos.Y + 4 * i);
                        CMouseClick();
                        i++;
                    }
                    posFlag = false;
                }
                Thread.Sleep(1000);
                ClearTextImg();
            }
        }
        /// 保存日志
        /// </summary>
        /// <param name="info">信息内容</param>
        /// <param name="str">文件名</param>
        public static void WriteLog(string info, string str)
        {
            string logDateDirPath = @"./Log/" + DateTime.Now.ToString("yyyyMMdd");

            string logFileName = logDateDirPath + "\\" + str + ".log"; ;

            if (!Directory.Exists(logDateDirPath))
            {
                Directory.CreateDirectory(logDateDirPath);
            }

            try
            {
                TextWriter tw = new StreamWriter(logFileName, true, System.Text.Encoding.Default); ;

                tw.WriteLine(info);

                tw.Close();
            }
            catch (Exception ex)
            {

            }
        }
        private void backgroundWorker1_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {

        }

        private void backgroundWorker1_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {

        }

        private void timer2_Tick(object sender, EventArgs e)
        {
            ClearMemory();
        }
    }
}

