using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using YukkuriMovieMaker.Commons;

namespace YMM4SamplePlugin.Easing
{
    internal class EasingEditorAttribute : PropertyEditorAttribute2
    {
        public override FrameworkElement Create() => new EasingEditor();

        public override void SetBindings(FrameworkElement control, ItemProperty[] itemProperties)
        {
            if (control is EasingEditor editor)
            {
                var owner = (CustomEasingEffect)itemProperties[0].PropertyOwner;
                editor.DataContext = new EasingEditorViewModel(owner);
            }
        }
        public override void ClearBindings(FrameworkElement control)
        {
            if (control.DataContext is IDisposable vm) vm.Dispose();
            control.DataContext = null;
        }
    }

    public class EasingEditorViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly CustomEasingEffect _owner;
        private bool _isApplyingTemplate = false;
        private readonly string _templateDir;
        private readonly string _settingsDir;
        private readonly string _sizeSettingsPath;


        public ObservableCollection<EasingTemplate> Templates { get; } = [];
        private EasingTemplate? _selectedTemplate;
        public EasingTemplate? SelectedTemplate
        {
            get => _selectedTemplate;
            set
            {
                if (Set(ref _selectedTemplate, value) && value != null)
                {
                    _isApplyingTemplate = true;
                    _owner.IsMidpointEnabled = value.IsMidpointEnabled;
                    _owner.MidpointTime = value.MidpointTime;
                    _owner.ControlPoint1 = new Vector2(value.CP1X, value.CP1Y);
                    _owner.ControlPoint2 = new Vector2(value.CP2X, value.CP2Y);
                    _owner.ControlPoint3 = new Vector2(value.CP3X, value.CP3Y);
                    _owner.ControlPoint4 = new Vector2(value.CP4X, value.CP4Y);
                    _isApplyingTemplate = false;
                    NewTemplateName = value.Name;
                }
                DeleteTemplateCommand.RaiseCanExecuteChanged();
            }
        }

        private string _newTemplateName = "新しいテンプレート";
        public string NewTemplateName { get => _newTemplateName; set => Set(ref _newTemplateName, value); }

        private double _editorSize = 300;
        public double EditorSize
        {
            get => _editorSize;
            set
            {
                if (Set(ref _editorSize, Math.Clamp(value, 150, 800)))
                {
                    OnPropertyChanged(nameof(EasingAreaSize));
                    PopulateGridLines();
                    SaveEditorSize();
                    _owner.EditorEasingAreaSize = (float)EasingAreaSize;
                }
            }
        }

        public double EasingAreaSize => EditorSize - 100;

        public bool IsMidpointEnabled { get => _owner.IsMidpointEnabled; set { if (_owner.IsMidpointEnabled != value) { _owner.IsMidpointEnabled = value; OnPropertyChanged(); } } }
        public double MidpointTime { get => _owner.MidpointTime; set { if (_owner.MidpointTime != value) { _owner.MidpointTime = value; OnPropertyChanged(); } } }
        public bool IsMidpointSmooth
        {
            get => _owner.IsMidpointSmooth;
            set
            {
                if (_owner.IsMidpointSmooth != value)
                {
                    _owner.IsMidpointSmooth = value;
                    if (value)
                    {
                        _owner.ControlPoint3 = -_owner.ControlPoint2;
                    }
                    OnPropertyChanged();
                }
            }
        }

        public Vector2 ControlPoint1 { get => _owner.ControlPoint1; set => _owner.ControlPoint1 = value; }
        public Vector2 ControlPoint2 { get => _owner.ControlPoint2; set => _owner.ControlPoint2 = value; }
        public Vector2 ControlPoint3 { get => _owner.ControlPoint3; set => _owner.ControlPoint3 = value; }
        public Vector2 ControlPoint4 { get => _owner.ControlPoint4; set => _owner.ControlPoint4 = value; }
        public bool ShowGrid { get => _owner.ShowGrid; set => _owner.ShowGrid = value; }
        public bool EnableSnapping { get => _owner.EnableSnapping; set => _owner.EnableSnapping = value; }

        public ActionCommand SaveTemplateCommand { get; }
        public ActionCommand DeleteTemplateCommand { get; }
        public ActionCommand AddMidpointCommand { get; }
        public ActionCommand RemoveMidpointCommand { get; }

        public ObservableCollection<double> VerticalGridLines { get; } = [];
        public ObservableCollection<double> HorizontalGridLines { get; } = [];

        public EasingEditorViewModel(CustomEasingEffect owner)
        {
            _owner = owner;
            ((INotifyPropertyChanged)_owner).PropertyChanged += OnOwnerPropertyChanged;
            var pluginPath = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";

            _templateDir = System.IO.Path.Combine(pluginPath, "EasingTemplates");
            Directory.CreateDirectory(_templateDir);

            _settingsDir = System.IO.Path.Combine(pluginPath, "settings");
            Directory.CreateDirectory(_settingsDir);
            _sizeSettingsPath = System.IO.Path.Combine(_settingsDir, "EasingSize.json");

            LoadEditorSize();
            _owner.EditorEasingAreaSize = (float)EasingAreaSize;

            SaveTemplateCommand = new ActionCommand(_ => !string.IsNullOrWhiteSpace(NewTemplateName), _ => SaveTemplate());
            DeleteTemplateCommand = new ActionCommand(_ => SelectedTemplate != null, _ => DeleteSelectedTemplate());
            AddMidpointCommand = new ActionCommand(_ => true, _ => IsMidpointEnabled = true);
            RemoveMidpointCommand = new ActionCommand(_ => true, _ => IsMidpointEnabled = false);

            LoadTemplates();
            PopulateGridLines();
        }

        private void LoadEditorSize()
        {
            if (!File.Exists(_sizeSettingsPath)) return;
            try
            {
                var json = File.ReadAllText(_sizeSettingsPath);
                var settings = JsonSerializer.Deserialize<EditorSizeSettings>(json);
                if (settings != null)
                {
                    _editorSize = settings.Size;
                }
            }
            catch { }
        }

        private void SaveEditorSize()
        {
            try
            {
                var settings = new EditorSizeSettings { Size = this.EditorSize };
                var json = JsonSerializer.Serialize(settings);
                File.WriteAllText(_sizeSettingsPath, json);
            }
            catch { }
        }


        private void PopulateGridLines()
        {
            VerticalGridLines.Clear();
            HorizontalGridLines.Clear();

            if (EasingAreaSize <= 0) return;

            double step = EasingAreaSize / 10.0;
            for (int i = 1; i < 10; i++)
            {
                var pos = i * step;
                VerticalGridLines.Add(pos);
                HorizontalGridLines.Add(pos);
            }
        }

        private void GenerateThumbnailGeometry(EasingTemplate template)
        {
            const double size = 32;
            var geometry = new PathGeometry();
            var figure = new PathFigure();

            var p0 = new Point(0, size);
            var pEnd = new Point(size, 0);

            double scale = size / 200.0;

            if (template.IsMidpointEnabled)
            {
                var midTimeX = template.MidpointTime * size;
                var pMid = new Point(midTimeX, size * 0.5);

                var cp1_abs = new Point(p0.X + template.CP1X * scale, p0.Y + template.CP1Y * scale);
                var cp2_abs = new Point(pMid.X + template.CP2X * scale, pMid.Y + template.CP2Y * scale);
                figure.StartPoint = p0;
                figure.Segments.Add(new BezierSegment(cp1_abs, cp2_abs, pMid, true));

                var cp3_abs = new Point(pMid.X + template.CP3X * scale, pMid.Y + template.CP3Y * scale);
                var cp4_abs = new Point(pEnd.X + template.CP4X * scale, pEnd.Y + template.CP4Y * scale);
                figure.Segments.Add(new BezierSegment(cp3_abs, cp4_abs, pEnd, true));
            }
            else
            {
                var cp1_abs = new Point(p0.X + template.CP1X * scale, p0.Y + template.CP1Y * scale);
                var cp2_abs = new Point(pEnd.X + template.CP2X * scale, pEnd.Y + template.CP2Y * scale);
                figure.StartPoint = p0;
                figure.Segments.Add(new BezierSegment(cp1_abs, cp2_abs, pEnd, true));
            }

            geometry.Figures.Add(figure);
            geometry.Freeze();
            template.CurveGeometry = geometry;
        }

        private void LoadTemplates()
        {
            Templates.Clear();
            foreach (var file in Directory.GetFiles(_templateDir, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var template = JsonSerializer.Deserialize<EasingTemplate>(json);
                    if (template != null)
                    {
                        template.FilePath = file;
                        GenerateThumbnailGeometry(template);
                        Templates.Add(template);
                    }
                }
                catch { }
            }
            DeleteTemplateCommand.RaiseCanExecuteChanged();
        }

        private void SaveTemplate()
        {
            var newTemplate = new EasingTemplate
            {
                Name = this.NewTemplateName,
                IsMidpointEnabled = _owner.IsMidpointEnabled,
                MidpointTime = _owner.MidpointTime,
                CP1X = _owner.ControlPoint1.X,
                CP1Y = _owner.ControlPoint1.Y,
                CP2X = _owner.ControlPoint2.X,
                CP2Y = _owner.ControlPoint2.Y,
                CP3X = _owner.ControlPoint3.X,
                CP3Y = _owner.ControlPoint3.Y,
                CP4X = _owner.ControlPoint4.X,
                CP4Y = _owner.ControlPoint4.Y,
            };

            GenerateThumbnailGeometry(newTemplate);

            var safeName = string.Join("_", newTemplate.Name.Split(System.IO.Path.GetInvalidFileNameChars()));
            var filePath = System.IO.Path.Combine(_templateDir, $"{safeName}.json");

            var options = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All) };
            var json = JsonSerializer.Serialize(newTemplate, options);
            File.WriteAllText(filePath, json);

            newTemplate.FilePath = filePath;

            var existing = Templates.FirstOrDefault(t => t.Name == newTemplate.Name);
            if (existing != null)
            {
                var index = Templates.IndexOf(existing);
                Templates[index] = newTemplate;
            }
            else
            {
                Templates.Add(newTemplate);
            }
            SelectedTemplate = newTemplate;
        }

        private void DeleteSelectedTemplate()
        {
            if (SelectedTemplate == null || string.IsNullOrEmpty(SelectedTemplate.FilePath)) return;
            try
            {
                if (File.Exists(SelectedTemplate.FilePath))
                {
                    File.Delete(SelectedTemplate.FilePath);
                }
                Templates.Remove(SelectedTemplate);
                SelectedTemplate = Templates.FirstOrDefault();
            }
            catch { }
        }

        private void OnOwnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_isApplyingTemplate) return;
            OnPropertyChanged(e.PropertyName);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; OnPropertyChanged(propertyName); return true; }
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)); }

        public void Dispose()
        {
            ((INotifyPropertyChanged)_owner).PropertyChanged -= OnOwnerPropertyChanged;
            LaunchUpdater();
        }

        private void LaunchUpdater()
        {
            try
            {
                string? pluginLocation = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(pluginLocation)) return;

                string updaterPath = System.IO.Path.Combine(pluginLocation, "Updater.exe");
                if (!File.Exists(updaterPath)) return;

                var ymmProcess = Process.GetCurrentProcess();

                var startInfo = new ProcessStartInfo(updaterPath)
                {
                    Arguments = $"{ymmProcess.Id} \"{pluginLocation}\"",
                    UseShellExecute = true,
                    CreateNoWindow = true
                };
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to launch updater: {ex.Message}");
            }
        }
    }

    internal class EditorSizeSettings
    {
        public double Size { get; set; }
    }


    public partial class EasingEditor : UserControl, IPropertyEditorControl
    {
        private EasingEditorViewModel? ViewModel => DataContext as EasingEditorViewModel;
        private Ellipse? _capturedElement;
        private double _initialEditorSize;
        private Vector2 _initialControlPoint1, _initialControlPoint2, _initialControlPoint3, _initialControlPoint4;

        public event EventHandler? BeginEdit;
        public event EventHandler? EndEdit;

        public EasingEditor()
        {
            InitializeComponent();
            this.DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is EasingEditorViewModel oldVm) oldVm.PropertyChanged -= OnViewModelPropertyChanged;
            if (e.NewValue is EasingEditorViewModel newVm)
            {
                newVm.PropertyChanged += OnViewModelPropertyChanged;
                UpdateGridVisibility();
                UpdateUI();
            }
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(EasingEditorViewModel.ShowGrid))
            {
                UpdateGridVisibility();
            }
            UpdateUI();
        }

        private void UpdateGridVisibility()
        {
            var vm = ViewModel;
            if (vm == null) return;
            var visibility = vm.ShowGrid ? Visibility.Visible : Visibility.Collapsed;
            VerticalGrid.Visibility = visibility;
            HorizontalGrid.Visibility = visibility;
        }

        private void Canvas_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateUI();

        private void UpdateUI()
        {
            var vm = ViewModel;
            if (vm == null || !vm.IsMidpointEnabled && EasingArea.ActualWidth <= 0) return;

            var w = vm.EasingAreaSize;
            var h = vm.EasingAreaSize;
            if (w <= 0) return;

            var p0 = new Point(0, h);
            var pEnd = new Point(w, 0);
            var midpointVisibility = vm.IsMidpointEnabled ? Visibility.Visible : Visibility.Collapsed;

            BezierFigure2_Path.Visibility = Handle2_1.Visibility = Handle2_2.Visibility = ControlPoint3.Visibility = ControlPoint4.Visibility = MidpointLine.Visibility = midpointVisibility;
            Handle3Text.Visibility = Handle4Text.Visibility = midpointVisibility;
            MidpointSliderPanel.Visibility = midpointVisibility;

            Handle1Text.Text = $"ハンドル1 (X:{vm.ControlPoint1.X:F0}, Y:{vm.ControlPoint1.Y:F0})";
            Handle2Text.Text = $"ハンドル2 (X:{vm.ControlPoint2.X:F0}, Y:{vm.ControlPoint2.Y:F0})";
            Handle3Text.Text = $"ハンドル3 (X:{vm.ControlPoint3.X:F0}, Y:{vm.ControlPoint3.Y:F0})";
            Handle4Text.Text = $"ハンドル4 (X:{vm.ControlPoint4.X:F0}, Y:{vm.ControlPoint4.Y:F0})";

            if (vm.IsMidpointEnabled)
            {
                var midTimeX = vm.MidpointTime * w;
                var pMid = new Point(midTimeX, h * 0.5);

                MidpointLine.X1 = midTimeX;
                MidpointLine.Y1 = 0;
                MidpointLine.X2 = midTimeX;
                MidpointLine.Y2 = h;
                Canvas.SetLeft(MidpointLine, 50);
                Canvas.SetTop(MidpointLine, 50);

                var cp1_abs = new Point(p0.X + vm.ControlPoint1.X, p0.Y + vm.ControlPoint1.Y);
                var cp2_abs = new Point(pMid.X + vm.ControlPoint2.X, pMid.Y + vm.ControlPoint2.Y);
                DrawBezier(BezierFigure1, Handle1_1, Handle1_2, ControlPoint1, ControlPoint2, p0, cp1_abs, cp2_abs, pMid);

                var cp3_abs = new Point(pMid.X + vm.ControlPoint3.X, pMid.Y + vm.ControlPoint3.Y);
                var cp4_abs = new Point(pEnd.X + vm.ControlPoint4.X, pEnd.Y + vm.ControlPoint4.Y);
                DrawBezier(BezierFigure2, Handle2_1, Handle2_2, ControlPoint3, ControlPoint4, pMid, cp3_abs, cp4_abs, pEnd);
            }
            else
            {
                var cp1_abs = new Point(p0.X + vm.ControlPoint1.X, p0.Y + vm.ControlPoint1.Y);
                var cp2_abs = new Point(pEnd.X + vm.ControlPoint2.X, pEnd.Y + vm.ControlPoint2.Y);
                DrawBezier(BezierFigure1, Handle1_1, Handle1_2, ControlPoint1, ControlPoint2, p0, cp1_abs, cp2_abs, pEnd);
                if (BezierFigure2.Segments.Count > 0)
                {
                    BezierFigure2.Segments.Clear();
                }
            }
        }

        private void ResizeThumb_DragStarted(object sender, DragStartedEventArgs e)
        {
            if (ViewModel is EasingEditorViewModel vm)
            {
                _initialEditorSize = vm.EditorSize;
                _initialControlPoint1 = vm.ControlPoint1;
                _initialControlPoint2 = vm.ControlPoint2;
                _initialControlPoint3 = vm.ControlPoint3;
                _initialControlPoint4 = vm.ControlPoint4;
            }
        }


        private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (ViewModel is EasingEditorViewModel vm)
            {
                double newSize = vm.EditorSize + e.HorizontalChange + e.VerticalChange;
                double scale = (newSize - 100) / (_initialEditorSize - 100);
                vm.EditorSize = newSize;

                if (_initialEditorSize > 100)
                {
                    vm.ControlPoint1 = _initialControlPoint1 * (float)scale;
                    vm.ControlPoint2 = _initialControlPoint2 * (float)scale;
                    vm.ControlPoint3 = _initialControlPoint3 * (float)scale;
                    vm.ControlPoint4 = _initialControlPoint4 * (float)scale;
                }
            }
        }

        private void DrawBezier(PathFigure figure, Line h1, Line h2, Ellipse c1, Ellipse c2, Point p0, Point p1, Point p2, Point p3)
        {
            var offset = new Point(50, 50);
            p0.Offset(offset.X, offset.Y);
            p1.Offset(offset.X, offset.Y);
            p2.Offset(offset.X, offset.Y);
            p3.Offset(offset.X, offset.Y);

            if (figure.Segments.FirstOrDefault() is not BezierSegment segment)
            {
                segment = new BezierSegment();
                figure.Segments.Add(segment);
            }
            figure.StartPoint = p0;
            segment.Point1 = p1; segment.Point2 = p2; segment.Point3 = p3;
            h1.X1 = p0.X; h1.Y1 = p0.Y; h1.X2 = p1.X; h1.Y2 = p1.Y;
            h2.X1 = p3.X; h2.Y1 = p3.Y; h2.X2 = p2.X; h2.Y2 = p2.Y;
            Canvas.SetLeft(c1, p1.X - 5); Canvas.SetTop(c1, p1.Y - 5);
            Canvas.SetLeft(c2, p2.X - 5); Canvas.SetTop(c2, p2.Y - 5);
        }

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.Source is Ellipse ellipse)
            {
                _capturedElement = ellipse;
                _capturedElement.CaptureMouse();
                BeginEdit?.Invoke(this, EventArgs.Empty);
            }
        }

        private void Canvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var vm = ViewModel;
            if (vm == null) return;

            var menu = new ContextMenu();
            MenuItem item;

            if (vm.IsMidpointEnabled)
            {
                item = new MenuItem { Header = "中間点を削除", Command = vm.RemoveMidpointCommand };
            }
            else
            {
                item = new MenuItem { Header = "中間点を追加", Command = vm.AddMidpointCommand };
            }
            menu.Items.Add(item);

            menu.IsOpen = true;
            e.Handled = true;
        }

        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_capturedElement != null)
            {
                _capturedElement.ReleaseMouseCapture();
                _capturedElement = null;
                EndEdit?.Invoke(this, EventArgs.Empty);
            }
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            var vm = ViewModel;
            if (_capturedElement == null || vm == null) return;
            var pos = e.GetPosition(EditorGrid);

            double canvasWidth = EditorGrid.ActualWidth;
            double canvasHeight = EditorGrid.ActualHeight;

            pos.X = Math.Clamp(pos.X, 0, canvasWidth);
            pos.Y = Math.Clamp(pos.Y, 0, canvasHeight);

            if (vm.EnableSnapping)
            {
                double easingAreaWidth = vm.EasingAreaSize;
                var gridUnit = easingAreaWidth / 10.0;
                var canvasOrigin = new Point(50, 50);
                pos.X = Math.Round((pos.X - canvasOrigin.X) / gridUnit) * gridUnit + canvasOrigin.X;
                pos.Y = Math.Round((pos.Y - canvasOrigin.Y) / gridUnit) * gridUnit + canvasOrigin.Y;
            }

            var w = vm.EasingAreaSize;
            var h = vm.EasingAreaSize;
            var p0_abs = new Point(50, h + 50);
            var pEnd_abs = new Point(w + 50, 50);
            var pMid_abs = new Point(50 + vm.MidpointTime * w, 50 + h * 0.5);
            float newOffsetX, newOffsetY;

            if (_capturedElement == ControlPoint1)
            {
                newOffsetX = (float)(pos.X - p0_abs.X);
                newOffsetY = (float)(pos.Y - p0_abs.Y);
                vm.ControlPoint1 = new Vector2(newOffsetX, newOffsetY);
            }
            else if (_capturedElement == ControlPoint2)
            {
                var anchor = vm.IsMidpointEnabled ? pMid_abs : pEnd_abs;
                newOffsetX = (float)(pos.X - anchor.X);
                newOffsetY = (float)(pos.Y - anchor.Y);
                vm.ControlPoint2 = new Vector2(newOffsetX, newOffsetY);

                if (vm.IsMidpointEnabled && vm.IsMidpointSmooth)
                {
                    vm.ControlPoint3 = -vm.ControlPoint2;
                }
            }
            else if (_capturedElement == ControlPoint3)
            {
                newOffsetX = (float)(pos.X - pMid_abs.X);
                newOffsetY = (float)(pos.Y - pMid_abs.Y);
                vm.ControlPoint3 = new Vector2(newOffsetX, newOffsetY);

                if (vm.IsMidpointEnabled && vm.IsMidpointSmooth)
                {
                    vm.ControlPoint2 = -vm.ControlPoint3;
                }
            }
            else if (_capturedElement == ControlPoint4)
            {
                newOffsetX = (float)(pos.X - pEnd_abs.X);
                newOffsetY = (float)(pos.Y - pEnd_abs.Y);
                vm.ControlPoint4 = new Vector2(newOffsetX, newOffsetY);
            }
        }
    }

    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is true) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is Visibility.Visible);
        }
    }
}