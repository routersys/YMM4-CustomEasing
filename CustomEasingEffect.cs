using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Text.Json.Serialization;
using System.Windows.Media;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;
using Vortice.Direct2D1;
using System.Globalization;

namespace YMM4SamplePlugin.Easing
{
    [VideoEffect("カスタム移動", ["移動"], [])]
    public class CustomEasingEffect : VideoEffectBase
    {
        public override string Label => "カスタム移動";

        [Display(Name = "イージングエディタ")]
        [EasingEditor(PropertyEditorSize = PropertyEditorSize.FullWidth)]
        public bool EditorPlaceholder { get; set; }

        [Display(Name = "終了 X", GroupName = "座標")]
        [AnimationSlider("F2", "px", -2000, 2000)]
        public Animation EndX { get; } = new(100, -100000, 100000);

        [Display(Name = "終了 Y", GroupName = "座標")]
        [AnimationSlider("F2", "px", -2000, 2000)]
        public Animation EndY { get; } = new(0, -100000, 100000);

        [Display(Name = "中間点 X", GroupName = "中間座標")]
        [AnimationSlider("F2", "px", -2000, 2000)]
        public Animation MidpointX { get; } = new(50, -100000, 100000);

        [Display(Name = "中間点 Y", GroupName = "中間座標")]
        [AnimationSlider("F2", "px", -2000, 2000)]
        public Animation MidpointY { get; } = new(-50, -100000, 100000);

        public bool IsMidpointEnabled { get => isMidpointEnabled; set => Set(ref isMidpointEnabled, value); }
        private bool isMidpointEnabled = false;

        public double MidpointTime { get => midpointTime; set => Set(ref midpointTime, Math.Clamp(value, 0.0, 1.0)); }
        private double midpointTime = 0.5;

        public Vector2 ControlPoint1 { get => controlPoint1; set => Set(ref controlPoint1, value); }
        private Vector2 controlPoint1 = new(40f, -40f);

        public Vector2 ControlPoint2 { get => controlPoint2; set => Set(ref controlPoint2, value); }
        private Vector2 controlPoint2 = new(-40f, 40f);

        public Vector2 ControlPoint3 { get => controlPoint3; set => Set(ref controlPoint3, value); }
        private Vector2 controlPoint3 = new(40f, -40f);

        public Vector2 ControlPoint4 { get => controlPoint4; set => Set(ref controlPoint4, value); }
        private Vector2 controlPoint4 = new(-40f, 40f);

        public bool ShowGrid { get => showGrid; set => Set(ref showGrid, value); }
        private bool showGrid = true;

        public bool EnableSnapping { get => enableSnapping; set => Set(ref enableSnapping, value); }
        private bool enableSnapping = true;

        public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices)
        {
            return new CustomEasingProcessor(this);
        }

        public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription exoOutputDescription)
        {
            return [];
        }

        protected override IEnumerable<IAnimatable> GetAnimatables() => [EndX, EndY, MidpointX, MidpointY];
    }

    public class CustomEasingProcessor : IVideoEffectProcessor
    {
        private readonly CustomEasingEffect _effect;
        private ID2D1Image? _input;
        private readonly BezierSolver _solver1;
        private readonly BezierSolver _solver2;

        public ID2D1Image Output => _input ?? throw new InvalidOperationException("Input image is not set.");

        public CustomEasingProcessor(CustomEasingEffect effect)
        {
            _effect = effect;
            _solver1 = new BezierSolver();
            _solver2 = new BezierSolver();
        }

        public DrawDescription Update(EffectDescription effectDescription)
        {
            var frame = effectDescription.ItemPosition.Frame;
            var length = effectDescription.ItemDuration.Frame;
            var fps = effectDescription.FPS;
            if (length == 0) return effectDescription.DrawDescription;

            var startPos = new Vector2(effectDescription.DrawDescription.Draw.X, effectDescription.DrawDescription.Draw.Y);
            var endPos = startPos + new Vector2((float)_effect.EndX.GetValue(frame, length, fps), (float)_effect.EndY.GetValue(frame, length, fps));
            Vector2 currentPos;

            double linearTime = (double)frame / length;
            const float editorSize = 200f;

            if (_effect.IsMidpointEnabled)
            {
                var midTime = (float)_effect.MidpointTime;
                var midPos = startPos + new Vector2((float)_effect.MidpointX.GetValue(frame, length, fps), (float)_effect.MidpointY.GetValue(frame, length, fps));

                var p0_1 = new Vector2(0, 0);
                var p3_1 = new Vector2(midTime, 0.5f);
                var p1_1_raw = p0_1 + new Vector2(_effect.ControlPoint1.X / editorSize, -_effect.ControlPoint1.Y / editorSize);
                var p2_1_raw = p3_1 + new Vector2(_effect.ControlPoint2.X / editorSize, -_effect.ControlPoint2.Y / editorSize);
                var p1_1 = new Vector2(Math.Clamp(p1_1_raw.X, 0f, midTime), p1_1_raw.Y);
                var p2_1 = new Vector2(Math.Clamp(p2_1_raw.X, 0f, midTime), p2_1_raw.Y);

                _solver1.Update(p1_1, p2_1, p0_1, p3_1);

                var p0_2 = new Vector2(midTime, 0.5f);
                var p3_2 = new Vector2(1, 1);
                var p1_2_raw = p0_2 + new Vector2(_effect.ControlPoint3.X / editorSize, -_effect.ControlPoint3.Y / editorSize);
                var p2_2_raw = p3_2 + new Vector2(_effect.ControlPoint4.X / editorSize, -_effect.ControlPoint4.Y / editorSize);
                var p1_2 = new Vector2(Math.Clamp(p1_2_raw.X, midTime, 1f), p1_2_raw.Y);
                var p2_2 = new Vector2(Math.Clamp(p2_2_raw.X, midTime, 1f), p2_2_raw.Y);

                _solver2.Update(p1_2, p2_2, p0_2, p3_2);

                if (linearTime <= midTime && midTime > 0)
                {
                    var progress = _solver1.Solve((float)linearTime);
                    currentPos = Vector2.Lerp(startPos, midPos, progress);
                }
                else
                {
                    var progress = _solver2.Solve((float)linearTime);
                    currentPos = Vector2.Lerp(midPos, endPos, progress);
                }
            }
            else
            {
                var p0 = new Vector2(0, 0);
                var p3 = new Vector2(1, 1);
                var p1_raw = p0 + new Vector2(_effect.ControlPoint1.X / editorSize, -_effect.ControlPoint1.Y / editorSize);
                var p2_raw = p3 + new Vector2(_effect.ControlPoint2.X / editorSize, -_effect.ControlPoint2.Y / editorSize);

                var p1 = new Vector2(Math.Clamp(p1_raw.X, 0f, 1f), p1_raw.Y);
                var p2 = new Vector2(Math.Clamp(p2_raw.X, 0f, 1f), p2_raw.Y);

                _solver1.Update(p1, p2, p0, p3);
                var progress = _solver1.Solve((float)linearTime);
                currentPos = Vector2.Lerp(startPos, endPos, progress);
            }

            var drawDesc = effectDescription.DrawDescription;
            return drawDesc with { Draw = drawDesc.Draw with { X = currentPos.X, Y = currentPos.Y } };
        }

        public void ClearInput() => _input = null;
        public void SetInput(ID2D1Image? input) => _input = input;
        public void Dispose() { }
    }

    public class BezierSolver
    {
        private const int NEWTON_ITERATIONS = 4;
        private const float NEWTON_MIN_SLOPE = 0.001f;
        private const float SUBDIVISION_PRECISION = 0.0000001f;
        private const int SUBDIVISION_MAX_ITERATIONS = 10;
        private const int kSplineTableSize = 11;
        private const float kSampleStepSize = 1.0f / (kSplineTableSize - 1.0f);

        private Vector2 _p1, _p2, _start, _end;
        private float[]? _sampleValues;

        public BezierSolver() { }
        public BezierSolver(Vector2 p1, Vector2 p2, Vector2 start, Vector2 end)
        {
            Update(p1, p2, start, end);
        }

        public void Update(Vector2 p1, Vector2 p2, Vector2 start, Vector2 end)
        {
            _p1 = p1;
            _p2 = p2;
            _start = start;
            _end = end;
            _sampleValues = null;
        }


        private float GetBezierCoordinate(float t, float p0, float p1, float p2, float p3)
        {
            float oneMinusT = 1 - t;
            return MathF.Pow(oneMinusT, 3) * p0 + 3 * MathF.Pow(oneMinusT, 2) * t * p1 + 3 * oneMinusT * MathF.Pow(t, 2) * p2 + MathF.Pow(t, 3) * p3;
        }

        private float GetSlope(float t, float p0, float p1, float p2, float p3)
        {
            float oneMinusT = 1 - t;
            return 3 * MathF.Pow(oneMinusT, 2) * (p1 - p0) + 6 * oneMinusT * t * (p2 - p1) + 3 * MathF.Pow(t, 2) * (p3 - p2);
        }

        private float GetTforX(float x)
        {
            if (_sampleValues == null)
            {
                _sampleValues = new float[kSplineTableSize];
                for (int i = 0; i < kSplineTableSize; ++i)
                {
                    _sampleValues[i] = GetBezierCoordinate(i * kSampleStepSize, _start.X, _p1.X, _p2.X, _end.X);
                }
            }


            float intervalStart = 0.0f;
            int currentSample = 1;
            int lastSample = kSplineTableSize - 1;

            for (; currentSample != lastSample && _sampleValues[currentSample] <= x; ++currentSample)
            {
                intervalStart += kSampleStepSize;
            }
            --currentSample;

            float denominator = _sampleValues[currentSample + 1] - _sampleValues[currentSample];
            float dist = denominator == 0 ? 0 : (x - _sampleValues[currentSample]) / denominator;
            float guessForT = intervalStart + dist * kSampleStepSize;

            float initialSlope = GetSlope(guessForT, _start.X, _p1.X, _p2.X, _end.X);
            if (initialSlope >= NEWTON_MIN_SLOPE)
            {
                for (int i = 0; i < NEWTON_ITERATIONS; ++i)
                {
                    float currentSlope = GetSlope(guessForT, _start.X, _p1.X, _p2.X, _end.X);
                    if (currentSlope == 0.0f)
                    {
                        return guessForT;
                    }
                    float currentX = GetBezierCoordinate(guessForT, _start.X, _p1.X, _p2.X, _end.X) - x;
                    guessForT -= currentX / currentSlope;
                }
                return guessForT;
            }
            else if (initialSlope == 0.0f)
            {
                return guessForT;
            }
            else
            {
                float a = intervalStart;
                float b = intervalStart + kSampleStepSize;
                float currentX;
                int i = 0;

                do
                {
                    guessForT = a + (b - a) / 2.0f;
                    currentX = GetBezierCoordinate(guessForT, _start.X, _p1.X, _p2.X, _end.X) - x;
                    if (currentX > 0.0f)
                    {
                        b = guessForT;
                    }
                    else
                    {
                        a = guessForT;
                    }
                } while (Math.Abs(currentX) > SUBDIVISION_PRECISION && ++i < SUBDIVISION_MAX_ITERATIONS);

                return guessForT;
            }
        }

        public float Solve(float x)
        {
            if (x <= _start.X) return _start.Y;
            if (x >= _end.X) return _end.Y;
            return GetBezierCoordinate(GetTforX(x), _start.Y, _p1.Y, _p2.Y, _end.Y);
        }
    }


    public class EasingTemplate
    {
        public string Name { get; set; } = "新規テンプレート";
        public bool IsMidpointEnabled { get; set; } = false;
        public double MidpointTime { get; set; } = 0.5;

        public float CP1X { get; set; }
        public float CP1Y { get; set; }
        public float CP2X { get; set; }
        public float CP2Y { get; set; }
        public float CP3X { get; set; }
        public float CP3Y { get; set; }
        public float CP4X { get; set; }
        public float CP4Y { get; set; }

        [JsonIgnore] public string FilePath { get; set; } = "";
        [JsonIgnore] public PathGeometry CurveGeometry { get; set; } = new();

        public EasingTemplate() { }
    }

    public static class EasingConverter
    {
        private const double EditorSize = 200.0;

        public static string[] ConvertToCss(CustomEasingEffect effect)
        {
            if (effect.IsMidpointEnabled)
            {
                var midTime = Math.Clamp(effect.MidpointTime, 0.0001, 0.9999);

                var cp1_orig_x = effect.ControlPoint1.X / EditorSize;
                var cp1_orig_y = -effect.ControlPoint1.Y / EditorSize;
                var cp2_orig_x = midTime + effect.ControlPoint2.X / EditorSize;
                var cp2_orig_y = 0.5 - effect.ControlPoint2.Y / EditorSize;

                var p1x = cp1_orig_x / midTime;
                var p1y = cp1_orig_y / 0.5;
                var p2x = cp2_orig_x / midTime;
                var p2y = cp2_orig_y / 0.5;

                var bezier1 = $"cubic-bezier({Format(p1x)}, {Format(p1y)}, {Format(p2x)}, {Format(p2y)})";

                var timeRange2 = 1.0 - midTime;

                var cp3_orig_x = midTime + effect.ControlPoint3.X / EditorSize;
                var cp3_orig_y = 0.5 - effect.ControlPoint3.Y / EditorSize;
                var cp4_orig_x = 1.0 + effect.ControlPoint4.X / EditorSize;
                var cp4_orig_y = 1.0 - effect.ControlPoint4.Y / EditorSize;

                var p3x = (cp3_orig_x - midTime) / timeRange2;
                var p3y = (cp3_orig_y - 0.5) / 0.5;
                var p4x = (cp4_orig_x - midTime) / timeRange2;
                var p4y = (cp4_orig_y - 0.5) / 0.5;

                var bezier2 = $"cubic-bezier({Format(p3x)}, {Format(p3y)}, {Format(p4x)}, {Format(p4y)})";

                return [bezier1, bezier2];
            }
            else
            {
                var p1x = effect.ControlPoint1.X / EditorSize;
                var p1y = -effect.ControlPoint1.Y / EditorSize;
                var p2x = 1.0 + effect.ControlPoint2.X / EditorSize;
                var p2y = 1.0 - effect.ControlPoint2.Y / EditorSize;

                var bezier = $"cubic-bezier({Format(p1x)}, {Format(p1y)}, {Format(p2x)}, {Format(p2y)})";

                return [bezier];
            }
        }

        private static string Format(double value)
        {
            return value.ToString("F4", CultureInfo.InvariantCulture);
        }
    }
}