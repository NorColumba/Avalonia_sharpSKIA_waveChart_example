using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using Avalonia.Utilities;
using SkiaSharp;

namespace AIAla.Views
{
    
    public class CustomSkiaPage : Control
    {
        private readonly GlyphRun _noSkia;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private double _fps;
        private int _frameCount;
        private readonly Stopwatch _fpsStopwatch = Stopwatch.StartNew();
        private Random _random = new();
        private const int WaveformCount = 12; // 预生成的波形数量
        private float[] _waveformData = new float[5000];
        private readonly float[][] _preGeneratedWaveforms;
        private int _currentWaveformIndex = 0;
        
        
        //委托
        public delegate void DrawWaveDelegate(SKCanvas canvas, Rect bounds);

        public CustomSkiaPage()
        {
            ClipToBounds = true;
            var text = "Current rendering API is not Skia";
            var glyphs = text.Select(ch => Typeface.Default.GlyphTypeface.GetGlyph(ch)).ToArray();
            _noSkia = new GlyphRun(Typeface.Default.GlyphTypeface, 12, text.AsMemory(), glyphs);
            _preGeneratedWaveforms = new float[WaveformCount][];
            GeneratePreWaveforms(); // 预生成波形数据
        }

        class CustomDrawOp : ICustomDrawOperation
        {
            private readonly IImmutableGlyphRunReference _noSkia;
            private readonly Action<SKCanvas, Rect> _drawWave;

            public CustomDrawOp(Rect bounds, GlyphRun noSkia, Action<SKCanvas, Rect> drawWave)
            {
                _noSkia = noSkia.TryCreateImmutableGlyphRunReference();
                Bounds = bounds;
                _drawWave = drawWave; // 保存绘制方法
            }

            public void Dispose()
            {
                // No-op
            }

            public Rect Bounds { get; }
            public bool HitTest(Point p) => false;
            public bool Equals(ICustomDrawOperation other) => false;
            private Rect ContextBoundRect { get; set; }
           
            
            
            
            public void Render(ImmediateDrawingContext context)
            {
                // 尝试获取 ISkiaSharpApiLeaseFeature 特性，以使用 SkiaSharp 相关 API
                var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
                // 如果未能获取到该特性，说明当前上下文中不支持 SkiaSharp API
                if (leaseFeature == null)
                    // 在这种情况下，绘制一个黑色的 'no Skia' 以指示 SkiaSharp 不可用
                    context.DrawGlyphRun(Brushes.Black, _noSkia);

                else
                {
                    // 使用using声明自动管理资源的释放，确保在使用完lease后正确释放资源
                    using var lease = leaseFeature.Lease();
                    // 获取lease关联的SkCanvas对象，用于后续的绘图操作
                    var canvas = lease.SkCanvas;
                    canvas.Clear(SKColors.Transparent);

                    // 使用传入的委托来绘制所有图像
                    _drawWave(canvas, Bounds);

                   
                }
            }
        }


        public override void Render(DrawingContext context)
        {
            context.Custom(new CustomDrawOp(
                new Rect(0, 0, Bounds.Width, Bounds.Height),
                _noSkia,
                DrawCustom
            ));
            Dispatcher.UIThread.InvokeAsync(InvalidateVisual, DispatcherPriority.Background);
        }


        public void DrawCustom(SKCanvas canvas, Rect bounds)
        {
            DrawXYAxis(canvas, bounds);
            //正弦
            //DrawSineWave(canvas, bounds);
            // 随机折线图 单次总点数：_waveformData.Length
            GenerateWaveformData();
            DrawRandomWaveform(canvas, bounds);
            DrawFPS(canvas, bounds);
        }

        
        private void GeneratePreWaveforms()
        {
            var random = new Random();

            for (int i = 0; i < WaveformCount; i++)
            {
                var data = new float[1000];

                for (int j = 0; j < 1000; j++)
                {
                    data[j] = (float)(random.NextDouble() * 2 - 1); // 范围 [-1, 1]
                }

                _preGeneratedWaveforms[i] = data;
            }
        }
        private void GenerateWaveformData()
        {
            // 直接引用预加载的数据，不复制
            _waveformData = _preGeneratedWaveforms[_currentWaveformIndex];

            // 切换到下一组数据（循环）
            _currentWaveformIndex = (_currentWaveformIndex + 1) % WaveformCount;
        }

        public void DrawRandomWaveform(SKCanvas canvas, Rect bounds)
        {
            float width = (float)bounds.Width;
            float height = (float)bounds.Height;

            float originY = height / 2f;
            float amplitude = height / 3f; // 幅度范围 ±1/3 高度

            using var path = new SKPath();
            float xStep = width / (_waveformData.Length - 1f);

            for (int i = 0; i < _waveformData.Length; i++)
            {
                float x = i * xStep;
                float y = originY - _waveformData[i] * amplitude;

                if (i == 0)
                {
                    path.MoveTo(x, y);
                }
                else
                {
                    path.LineTo(x, y);
                }
            }

            using var paint = new SKPaint
            {
                Color = SKColor.Parse("Ffff00"),
                StrokeWidth = 2,
                Style = SKPaintStyle.Stroke,
                IsAntialias = true
            };

            canvas.DrawPath(path, paint);
        }
        
        private void DrawFPS(SKCanvas canvas, Rect bounds)
        {
            float width = (float)bounds.Width;

            using var textPaint = new SKPaint
            {
                Color = SKColors.LightGreen,
                TextSize = 16,
                IsAntialias = true,
                TextAlign = SKTextAlign.Right
            };

            string fpsText = $"FPS: {_fps:F1}";
            canvas.DrawText(fpsText, width - 10, 25, textPaint);

            // 更新帧率计数
            _frameCount++;
            if (_fpsStopwatch.Elapsed.TotalSeconds >= 1)
            {
                _fps = _frameCount / _fpsStopwatch.Elapsed.TotalSeconds;
                _frameCount = 0;
                _fpsStopwatch.Restart();
            }
        }
        
        public void DrawXYAxis(SKCanvas canvas, Rect bounds)
        {
            float width = (float)bounds.Width;
            float height = (float)bounds.Height;

            var origin = new SKPoint(width / 2, height / 2);

            // 背景画笔
            using var backgroundPaint = new SKPaint { Color = SKColors.Black };
            canvas.DrawRect(new SKRect(0, 0, width, height), backgroundPaint);

            // 网格线与刻度样式
            using var gridPaint = new SKPaint { Color = new SKColor(40, 40, 40), StrokeWidth = 1 };
            using var axisPaint = new SKPaint { Color = SKColors.LightGray, StrokeWidth = 2 };
            using var majorTickPaint = new SKPaint { Color = SKColors.White, StrokeWidth = 2 };
            using var minorTickPaint = new SKPaint { Color = new SKColor(80, 80, 80), StrokeWidth = 1 };
            using var textPaint = new SKPaint
            {
                Color = SKColors.LightGray,
                TextSize = 14,
                TextAlign = SKTextAlign.Center,
                IsAntialias = true
            };

            // 动态确定主刻度间隔（每约 100 像素一个主刻度）
            int majorStepPx = 100;
            int minorStepPx = 20;

            // X轴方向绘制
            for (int x = (int)origin.X; x < width; x += minorStepPx)
            {
                bool isMajor = (x - (int)origin.X) % majorStepPx == 0;
                var paint = isMajor ? majorTickPaint : minorTickPaint;
                canvas.DrawLine(x, origin.Y - 5, x, origin.Y + 5, paint);
                if (isMajor)
                {
                    float logicalX = (x - origin.X) / 50f; // 比例换算成逻辑值
                    canvas.DrawText($"{logicalX:F1}", x, origin.Y - 10, textPaint);
                }
            }

            for (int x = (int)origin.X; x > 0; x -= minorStepPx)
            {
                bool isMajor = ((int)origin.X - x) % majorStepPx == 0;
                var paint = isMajor ? majorTickPaint : minorTickPaint;
                canvas.DrawLine(x, origin.Y - 5, x, origin.Y + 5, paint);
                if (isMajor)
                {
                    float logicalX = (x - origin.X) / 50f;
                    canvas.DrawText($"{logicalX:F1}", x, origin.Y - 10, textPaint);
                }
            }

            // Y轴方向绘制
            for (int y = (int)origin.Y; y < height; y += minorStepPx)
            {
                bool isMajor = ((int)origin.Y - y) % majorStepPx == 0;
                var paint = isMajor ? majorTickPaint : minorTickPaint;
                canvas.DrawLine(origin.X - 5, y, origin.X + 5, y, paint);
                if (isMajor)
                {
                    float logicalY = -(y - origin.Y) / 50f;
                    canvas.DrawText($"{logicalY:F1}", origin.X + 20, y + 5, textPaint);
                }
            }

            for (int y = (int)origin.Y; y > 0; y -= minorStepPx)
            {
                bool isMajor = ((int)origin.Y - y) % majorStepPx == 0;
                var paint = isMajor ? majorTickPaint : minorTickPaint;
                canvas.DrawLine(origin.X - 5, y, origin.X + 5, y, paint);
                if (isMajor)
                {
                    float logicalY = -(y - origin.Y) / 50f;
                    canvas.DrawText($"{logicalY:F1}", origin.X + 20, y + 5, textPaint);
                }
            }

            // 主轴线
            canvas.DrawLine(0, origin.Y, width, origin.Y, axisPaint); // X Axis
            canvas.DrawLine(origin.X, 0, origin.X, height, axisPaint); // Y Axis
        }


        public void DrawSineWave(SKCanvas canvas, Rect bounds)
        {
            float width = (float)bounds.Width;
            float height = (float)bounds.Height;

            float amplitude = height / 3f; // 振幅
            float frequency = 0.02f; // 频率
            float phase = (float)(_stopwatch.Elapsed.TotalSeconds * 100); // 动态相位

            using var path = new SKPath();
            for (float x = 0; x < width; x++)
            {
                float y = (float)(amplitude * Math.Sin(frequency * (x + phase)) + height / 2);
                if (x == 0)
                    path.MoveTo(x, y);
                else
                    path.LineTo(x, y);
            }

            using var paint = new SKPaint
            {
                Color = SKColor.Parse("4A90E2"), // 蓝色波形
                StrokeWidth = 3,
                Style = SKPaintStyle.Stroke,
                IsAntialias = true
            };

            canvas.DrawPath(path, paint);
        }
    }
}