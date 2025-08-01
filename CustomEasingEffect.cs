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
        private readonly BezierSolver _solver1 = new();
        private readonly BezierSolver _solver2 = new();

        public ID2D1Image Output => _input ?? throw new InvalidOperationException("Input image is not set.");

        public CustomEasingProcessor(CustomEasingEffect effect)
        {
            _effect = effect;
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
                var midTime = _effect.MidpointTime;
                var midPos = startPos + new Vector2((float)_effect.MidpointX.GetValue(frame, length, fps), (float)_effect.MidpointY.GetValue(frame, length, fps));

                var p0_norm = new Vector2(0, 0);
                var pMid_norm = new Vector2((float)midTime, 0.5f);
                var pEnd_norm = new Vector2(1, 1);

                if (linearTime <= midTime && midTime > 0)
                {
                    var t = (float)(linearTime / midTime);
                    var p1_norm_abs = p0_norm + new Vector2(_effect.ControlPoint1.X / editorSize, -_effect.ControlPoint1.Y / editorSize);
                    var p2_norm_abs = pMid_norm + new Vector2(_effect.ControlPoint2.X / editorSize, -_effect.ControlPoint2.Y / editorSize);
                    var progress = _solver1.Solve(t, p1_norm_abs, p2_norm_abs, p0_norm, pMid_norm);
                    currentPos = Vector2.Lerp(startPos, midPos, progress);
                }
                else
                {
                    var t = midTime >= 1 ? 1 : (float)((linearTime - midTime) / (1 - midTime));
                    var p1_norm_abs = pMid_norm + new Vector2(_effect.ControlPoint3.X / editorSize, -_effect.ControlPoint3.Y / editorSize);
                    var p2_norm_abs = pEnd_norm + new Vector2(_effect.ControlPoint4.X / editorSize, -_effect.ControlPoint4.Y / editorSize);
                    var progress = _solver2.Solve(t, p1_norm_abs, p2_norm_abs, pMid_norm, pEnd_norm);
                    currentPos = Vector2.Lerp(midPos, endPos, progress);
                }
            }
            else
            {
                var p0_norm = new Vector2(0, 0);
                var pEnd_norm = new Vector2(1, 1);
                var p1_norm_abs = p0_norm + new Vector2(_effect.ControlPoint1.X / editorSize, -_effect.ControlPoint1.Y / editorSize);
                var p2_norm_abs = pEnd_norm + new Vector2(_effect.ControlPoint2.X / editorSize, -_effect.ControlPoint2.Y / editorSize);
                var progress = _solver1.Solve((float)linearTime, p1_norm_abs, p2_norm_abs, p0_norm, pEnd_norm);
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
        private static float GetX(float t, Vector2 p1, Vector2 p2, Vector2 start, Vector2 end) =>
            3 * MathF.Pow(1 - t, 2) * t * p1.X +
            3 * (1 - t) * t * t * p2.X +
            MathF.Pow(1 - t, 3) * start.X + MathF.Pow(t, 3) * end.X;

        private static float GetY(float t, Vector2 p1, Vector2 p2, Vector2 start, Vector2 end) =>
            3 * MathF.Pow(1 - t, 2) * t * p1.Y +
            3 * (1 - t) * t * t * p2.Y +
            MathF.Pow(1 - t, 3) * start.Y + MathF.Pow(t, 3) * end.Y;

        public float Solve(float time, Vector2 p1, Vector2 p2, Vector2 start, Vector2 end)
        {
            if (time <= 0.0f) return start.Y;
            if (time >= 1.0f) return end.Y;

            float t = time;
            for (int i = 0; i < 8; i++)
            {
                float x = GetX(t, p1, p2, start, end);
                float dx_dt = 3 * (1 - t) * (1 - t) * (p1.X - start.X) + 6 * (1 - t) * t * (p2.X - p1.X) + 3 * t * t * (end.X - p2.X);
                if (MathF.Abs(x - time) < 1e-6f) break;
                if (MathF.Abs(dx_dt) < 1e-6f) break;
                t -= (x - time) / dx_dt;
            }
            return GetY(Math.Clamp(t, 0, 1), p1, p2, start, end);
        }
    }

    public class EasingTemplate
    {
        public string Name { get; set; } = "新規テンプレート";
        public bool IsMidpointEnabled { get; set; } = false;
        public double MidpointTime { get; set; } = 0.5;

        // Vector2を直接シリアライズする代わりに、個別のfloatプロパティをシリアライズする
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
}