using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EpubReader.Core;
using EpubReader.Rendering;
using Microsoft.Win32;

namespace EpubReader.App;

public partial class MainWindow : Window
{
    ReaderSession? _session;
    RenderHost? _render;
    readonly AppState _state = ProgressStore.Load();
    int _theme;
    int _viewerIndex = -1;
    bool _busy;
    int _illustGen;
    bool _illustReady;
    readonly Dictionary<string, BitmapSource> _thumbCache = new(StringComparer.OrdinalIgnoreCase);
    readonly System.Windows.Threading.DispatcherTimer _resizeTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(80)
    };

    static readonly uint[] Paper = [0xFFF3EAD3, 0xFF1A1612, 0xFFFFFFFF];
    static readonly uint[] Ink = [0xFF2A2118, 0xFFE8DCC4, 0xFF1A1612];
    static readonly string[] ThemeName = ["暖紙", "夜讀", "預設"];
    bool _menuOpen;
    bool _updatingSlider;
    bool _sliderDragging;

    public MainWindow()
    {
        InitializeComponent();
        _theme = Math.Clamp(_state.Theme, 0, 2);
        ApplyThemeColors();
        ApplyChrome();
        _resizeTimer.Tick += (_, _) =>
        {
            _resizeTimer.Stop();
            Relayout();
        };
        Loaded += (_, _) =>
        {
            ApplyChrome();
            var args = Environment.GetCommandLineArgs();
            if (args.Length > 1)
                _ = OpenPath(args[1]);
        };
    }

    async void OnOpen(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "書籍|*.epub;*.cbz;*.zip;*.cb7;*.7z;*.cbr;*.rar|EPUB|*.epub|漫畫壓縮檔|*.cbz;*.zip;*.7z;*.cb7;*.cbr;*.rar|所有檔案|*.*"
        };
        if (dlg.ShowDialog() == true)
            await OpenPath(dlg.FileName);
    }

    public async Task OpenPath(string path)
    {
        if (_busy) return;
        _busy = true;
        EmptyHint.Text = "開啟中…";
        EmptyHint.Visibility = Visibility.Visible;
        try
        {
            SaveCurrent();
            _session?.Dispose();
            _session = null;
            var threshold = _state.ImageThreshold > 0 ? _state.ImageThreshold : 500;
            var book = await Task.Run(() => BookLoader.Open(path, threshold));
            _state.Books.TryGetValue(ProgressStore.KeyFor(path), out var progress);
            _session = new ReaderSession(book, progress);
            ApplyThemeColors();
            _render?.Dispose();
            _render = new RenderHost(CjkFonts.DefaultFamily(book.Language), book.Language);
            _session.Settings.FontFamily = _render.FontFamily;
            EmptyHint.Visibility = Visibility.Collapsed;
            Title = book.Title + " — EpubReader";
            Relayout();
            BuildToc();
            ResetIllustAlbum();
            TocPanel.Visibility = Visibility.Collapsed;
            IllustPanel.Visibility = Visibility.Collapsed;
            ImageViewer.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "無法開啟", MessageBoxButton.OK, MessageBoxImage.Warning);
            EmptyHint.Text = "將 EPUB / CBZ 拖進來\n或按 O 開檔";
            EmptyHint.Visibility = Visibility.Visible;
        }
        finally
        {
            _busy = false;
        }
    }

    void Relayout()
    {
        if (_session == null || _render == null) return;
        ApplyProgressChrome();
        ApplyChrome();
        UpdateLayout();
        double w = PageImage.ActualWidth;
        double h = PageImage.ActualHeight;
        if (w < 8 || h < 8)
        {
            w = Math.Max(320, ActualWidth);
            h = Math.Max(320, ActualHeight);
        }
        _session.Settings.PageWidth = (float)w;
        _session.Settings.PageHeight = (float)h;
        float em = 26f * Math.Max(0.6f, _session.Settings.FontScale);
        _session.Settings.Margin = em * 2f;
        _session.Relayout(_render);
        Present();
    }

    void Present()
    {
        if (_session == null || _render == null) return;
        var vis = _session.VisiblePageIndices();
        if (vis.Count == 0) return;
        _session.Settings.VisualLtr = _session.VisualLtr;
        var pages = vis.Select(i => _session.Pages[i]).ToList();
        var dpi = VisualTreeHelper.GetDpi(this);
        var rendered = _render.Render(pages, _session.Settings, k => _session.Book.LoadResource?.Invoke(k), (float)dpi.DpiScaleX);
        var bmp = new WriteableBitmap(rendered.Width, rendered.Height, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Bgra32, null);
        bmp.Lock();
        try
        {
            Marshal.Copy(rendered.Bgra, 0, bmp.BackBuffer, rendered.Bgra.Length);
            bmp.AddDirtyRect(new Int32Rect(0, 0, rendered.Width, rendered.Height));
        }
        finally
        {
            bmp.Unlock();
        }
        bmp.Freeze();
        PageImage.Source = bmp;
        BtnVertical.Content = _session.Settings.WritingMode == WritingMode.Vertical ? "直排" : "橫排";
        BtnSpread.Content = _session.Settings.Spread ? "雙頁" : "單頁";
        BtnSpread.Visibility = _session.Book.IsManga || pages.Any(p => p.IsImagePage) ? Visibility.Visible : Visibility.Collapsed;
        int idx = vis[0];
        StatusText.Text = $"{_session.Book.Title}  ·  {idx + 1}/{_session.Pages.Count}";
        ApplyProgressChrome();
        if (!_sliderDragging)
        {
            double max = Math.Max(1, _session.Pages.Count - 1);
            _updatingSlider = true;
            ProgressSlider.Maximum = max;
            ProgressSlider.Value = idx;
            SideSlider.Maximum = max;
            SideSlider.Value = idx;
            _updatingSlider = false;
        }
    }

    bool VisualLtr() => _session?.VisualLtr ?? true;

    void ApplyProgressChrome()
    {
        bool ltr = VisualLtr();
        BottomBar.Visibility = Visibility.Visible;
        SideBar.Visibility = Visibility.Collapsed;
        // 直排／右左：右→左；橫排：左→右。
        ProgressSlider.IsDirectionReversed = !ltr;
    }

    void ApplyChrome()
    {
        Toolbar.Visibility = _menuOpen ? Visibility.Visible : Visibility.Collapsed;
        ApplyProgressChrome();
    }

    void ToggleMenu()
    {
        _menuOpen = !_menuOpen;
        if (!_menuOpen)
        {
            TocPanel.Visibility = Visibility.Collapsed;
            IllustPanel.Visibility = Visibility.Collapsed;
        }
        ApplyChrome();
    }

    void CloseMenu()
    {
        if (!_menuOpen) return;
        _menuOpen = false;
        TocPanel.Visibility = Visibility.Collapsed;
        IllustPanel.Visibility = Visibility.Collapsed;
        ApplyChrome();
    }

    void OnToggleChrome(object sender, RoutedEventArgs e) => ToggleMenu();

    void BuildToc()
    {
        TocList.Items.Clear();
        if (_session == null) return;
        void Add(IEnumerable<TocItem> items, string prefix)
        {
            foreach (var it in items)
            {
                TocList.Items.Add(new TocRow($"{prefix}{it.Title}", it.SpineIndex));
                if (it.Children.Count > 0)
                    Add(it.Children, prefix + "　");
            }
        }
        Add(_session.Book.Toc, "");
    }

    void ResetIllustAlbum()
    {
        _illustGen++;
        _illustReady = false;
        _thumbCache.Clear();
        IllustWrap.Children.Clear();
        IllustEmpty.Visibility = Visibility.Collapsed;
        IllustCount.Text = "";
    }

    void EnsureIllustAlbum()
    {
        if (_illustReady || _session == null) return;
        _illustReady = true;
        BuildIllust();
    }

    void BuildIllust()
    {
        IllustWrap.Children.Clear();
        if (_session == null) return;
        var list = _session.Book.Illustrations;
        IllustCount.Text = list.Count == 0 ? "" : $"{list.Count} 張";
        IllustEmpty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        IllustScroll.Visibility = list.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (list.Count == 0) return;

        _illustGen++;
        int gen = _illustGen;
        var pending = new List<(Illustration Ill, Image Img)>(list.Count);
        foreach (var ill in list)
        {
            var img = new Image
            {
                Stretch = Stretch.Uniform,
                Margin = new Thickness(8, 8, 8, 0)
            };
            if (_thumbCache.TryGetValue(ill.ResourceKey, out var cached))
                img.Source = cached;
            var caption = new TextBlock
            {
                Text = ill.Label,
                FontSize = 11,
                Opacity = 0.72,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(8, 4, 8, 8)
            };
            var tile = new DockPanel();
            DockPanel.SetDock(caption, Dock.Bottom);
            tile.Children.Add(caption);
            tile.Children.Add(img);
            var btn = new Button
            {
                Width = 168,
                Height = 228,
                Margin = new Thickness(4),
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x1A, 0x16)),
                Tag = ill,
                ToolTip = $"{ill.Label}  {ill.Width}×{ill.Height}",
                Content = tile
            };
            var captured = ill;
            btn.Click += (_, _) => OpenIllustration(captured);
            btn.MouseDoubleClick += (_, e) =>
            {
                e.Handled = true;
                JumpToIllustration(captured);
            };
            IllustWrap.Children.Add(btn);
            if (img.Source == null)
                pending.Add((ill, img));
        }
        if (pending.Count > 0)
            _ = LoadThumbsAsync(gen, pending);
    }

    async Task LoadThumbsAsync(int gen, List<(Illustration Ill, Image Img)> pending)
    {
        foreach (var (ill, img) in pending)
        {
            if (gen != _illustGen || _session == null) return;
            var key = ill.ResourceKey;
            byte[]? bytes = null;
            try
            {
                bytes = await Task.Run(() => _session?.Book.LoadResource?.Invoke(key)).ConfigureAwait(true);
            }
            catch
            {
                continue;
            }
            if (bytes == null || gen != _illustGen) continue;
            var thumb = DecodeThumb(bytes, 220);
            if (thumb == null || gen != _illustGen) continue;
            _thumbCache[key] = thumb;
            img.Source = thumb;
            await System.Windows.Threading.Dispatcher.Yield();
        }
    }

    static BitmapSource? DecodeThumb(byte[] bytes, int pixelWidth)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.DecodePixelWidth = Math.Max(64, pixelWidth);
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    void OpenIllustration(Illustration ill)
    {
        if (_session == null) return;
        _viewerIndex = _session.Book.Illustrations.ToList().FindIndex(x => x.ResourceKey == ill.ResourceKey);
        ShowViewer();
    }

    void JumpToIllustration(Illustration ill)
    {
        if (_session == null) return;
        _session.GoToIllustration(ill);
        ImageViewer.Visibility = Visibility.Collapsed;
        IllustPanel.Visibility = Visibility.Collapsed;
        Present();
    }

    void ShowViewer()
    {
        if (_session == null || _viewerIndex < 0) return;
        var list = _session.Book.Illustrations;
        if (_viewerIndex >= list.Count) return;
        var ill = list[_viewerIndex];
        var bytes = _session.Book.LoadResource?.Invoke(ill.ResourceKey);
        if (bytes == null) return;
        using var ms = new MemoryStream(bytes);
        var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        ViewerImage.Source = decoder.Frames[0];
        ImageViewer.Visibility = Visibility.Visible;
    }

    void HideOverlays()
    {
        if (ImageViewer.Visibility == Visibility.Visible)
        {
            ImageViewer.Visibility = Visibility.Collapsed;
            return;
        }
        if (TocPanel.Visibility == Visibility.Visible || IllustPanel.Visibility == Visibility.Visible)
        {
            TocPanel.Visibility = Visibility.Collapsed;
            IllustPanel.Visibility = Visibility.Collapsed;
            return;
        }
        if (WindowState == WindowState.Maximized && WindowStyle == WindowStyle.None)
            ExitFullscreen();
    }

    void OnToggleToc(object sender, RoutedEventArgs e)
    {
        TocPanel.Visibility = TocPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        IllustPanel.Visibility = Visibility.Collapsed;
    }

    void OnToggleIllust(object sender, RoutedEventArgs e)
    {
        if (IllustPanel.Visibility == Visibility.Visible)
        {
            IllustPanel.Visibility = Visibility.Collapsed;
            return;
        }
        TocPanel.Visibility = Visibility.Collapsed;
        IllustPanel.Visibility = Visibility.Visible;
        EnsureIllustAlbum();
    }

    void OnToggleVertical(object sender, RoutedEventArgs e)
    {
        if (_session == null) return;
        _session.ToggleWritingMode();
        Relayout();
    }

    void OnToggleSpread(object sender, RoutedEventArgs e)
    {
        if (_session == null) return;
        _session.Settings.Spread = !_session.Settings.Spread;
        Present();
    }

    void OnFontUp(object sender, RoutedEventArgs e)
    {
        if (_session == null) return;
        _session.Settings.FontScale = Math.Min(2.4f, _session.Settings.FontScale + 0.1f);
        Relayout();
    }

    void OnFontDown(object sender, RoutedEventArgs e)
    {
        if (_session == null) return;
        _session.Settings.FontScale = Math.Max(0.6f, _session.Settings.FontScale - 0.1f);
        Relayout();
    }

    void OnTheme(object sender, RoutedEventArgs e)
    {
        _theme = (_theme + 1) % 3;
        _state.Theme = _theme;
        ApplyThemeColors();
        ProgressStore.Save(_state);
        Relayout();
    }

    void ApplyThemeColors()
    {
        BtnTheme.Content = ThemeName[_theme];
        var paper = Paper[_theme];
        var ink = Ink[_theme];
        var paperBrush = new SolidColorBrush(Color.FromRgb(
            (byte)((paper >> 16) & 0xFF),
            (byte)((paper >> 8) & 0xFF),
            (byte)(paper & 0xFF)));
        var inkBrush = new SolidColorBrush(Color.FromRgb(
            (byte)((ink >> 16) & 0xFF),
            (byte)((ink >> 8) & 0xFF),
            (byte)(ink & 0xFF)));
        paperBrush.Freeze();
        inkBrush.Freeze();
        Background = paperBrush;
        Foreground = inkBrush;
        Toolbar.Background = paperBrush;
        TocPanel.Background = paperBrush;
        IllustPanel.Background = paperBrush;
        if (_session == null) return;
        _session.Settings.PaperArgb = paper;
        _session.Settings.InkArgb = ink;
    }

    void OnTocClick(object sender, MouseButtonEventArgs e) => JumpToc();
    void OnTocKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) JumpToc();
    }

    void JumpToc()
    {
        if (_session == null) return;
        if (TocList.SelectedItem is TocRow row)
        {
            _session.GoToSpine(row.SpineIndex);
            TocPanel.Visibility = Visibility.Collapsed;
            Present();
        }
    }

    void OnSliderPreviewDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Slider slider) return;
        _sliderDragging = true;
        slider.CaptureMouse();
        ApplySliderPoint(slider, e);
        e.Handled = true;
    }

    void OnSliderPreviewMove(object sender, MouseEventArgs e)
    {
        if (!_sliderDragging || e.LeftButton != MouseButtonState.Pressed) return;
        if (sender is Slider slider)
            ApplySliderPoint(slider, e);
    }

    void OnSliderPreviewUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Slider slider && slider.IsMouseCaptured)
            slider.ReleaseMouseCapture();
        _sliderDragging = false;
        JumpSlider(sender as Slider);
    }

    void OnSliderLostCapture(object sender, MouseEventArgs e)
    {
        _sliderDragging = false;
    }

    void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingSlider) return;
        JumpSlider(sender as Slider);
    }

    void ApplySliderPoint(Slider slider, MouseEventArgs e)
    {
        var p = e.GetPosition(slider);
        double len = slider.Orientation == Orientation.Horizontal ? slider.ActualWidth : slider.ActualHeight;
        if (len < 1) return;
        double t = slider.Orientation == Orientation.Horizontal ? p.X / len : p.Y / len;
        t = Math.Clamp(t, 0, 1);
        // 直向預設下端是最小值；反向軸再對調一次。
        if (slider.Orientation == Orientation.Vertical)
            t = 1 - t;
        if (slider.IsDirectionReversed)
            t = 1 - t;
        slider.Value = slider.Minimum + t * (slider.Maximum - slider.Minimum);
    }

    void JumpSlider(Slider? slider)
    {
        if (_session == null || slider == null) return;
        _session.PageIndex = (int)Math.Round(slider.Value);
        Present();
    }

    void OnPageClick(object sender, MouseButtonEventArgs e)
    {
        if (TryFollowLink(e.GetPosition(PageImage)))
        {
            e.Handled = true;
            return;
        }
        var win = e.GetPosition(this);
        if (win.Y < ActualHeight * 0.2)
        {
            ToggleMenu();
            e.Handled = true;
            return;
        }
        if (_menuOpen)
        {
            CloseMenu();
            e.Handled = true;
        }
    }

    bool TryFollowLink(Point pagePos)
    {
        if (_session == null) return false;
        var vis = _session.VisiblePageIndices();
        if (vis.Count == 0) return false;
        float half = _session.Settings.PageWidth / 2f;
        bool ltr = _session.VisualLtr;
        for (int vi = 0; vi < vis.Count; vi++)
        {
            float xOff = vis.Count == 1 ? 0 : ltr ? (vi == 0 ? 0 : half) : (vi == 0 ? half : 0);
            var page = _session.Pages[vis[vi]];
            foreach (var op in page.Ops)
            {
                string? href = op switch
                {
                    DrawTextOp t => t.Href,
                    DrawRubyOp r => r.Href,
                    _ => null
                };
                if (string.IsNullOrEmpty(href)) continue;
                var (x, y, w, h) = op switch
                {
                    DrawTextOp t => (t.X, t.Y, t.Width, t.Height),
                    DrawRubyOp r => (r.X, r.Y, r.Width, r.Height),
                    _ => (0f, 0f, 0f, 0f)
                };
                const float pad = 4f;
                if (pagePos.X < xOff + x - pad || pagePos.X > xOff + x + w + pad) continue;
                if (pagePos.Y < y - pad || pagePos.Y > y + h + pad) continue;
                var from = _session.Book.Spine[page.SpineIndex].Key;
                if (_session.GoToHref(href, from))
                {
                    Present();
                    return true;
                }
            }
        }
        return false;
    }

    void Turn(bool next)
    {
        if (_session == null) return;
        if (next) _session.NextPage();
        else _session.PrevPage();
        Present();
    }

    void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (_session == null) return;
        if (ImageViewer.Visibility == Visibility.Visible
            || IllustPanel.Visibility == Visibility.Visible
            || TocPanel.Visibility == Visibility.Visible)
            return;
        e.Handled = true;
        Turn(e.Delta < 0);
    }

    void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_session == null) return;
        _resizeTimer.Stop();
        _resizeTimer.Start();
    }

    void OnViewerClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == ViewerImage || e.Source is Button) return;
        e.Handled = true;
        ImageViewer.Visibility = Visibility.Collapsed;
    }

    void OnViewerImageDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            e.Handled = true;
            JumpCurrentViewer();
        }
    }

    void OnJumpViewer(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        JumpCurrentViewer();
    }

    void JumpCurrentViewer()
    {
        if (_session == null || _viewerIndex < 0) return;
        var list = _session.Book.Illustrations;
        if (_viewerIndex >= list.Count) return;
        JumpToIllustration(list[_viewerIndex]);
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            _ = OpenPath(files[0]);
    }

    void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ImageViewer.Visibility == Visibility.Visible)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    ImageViewer.Visibility = Visibility.Collapsed;
                    e.Handled = true;
                    break;
                case Key.Enter:
                case Key.G:
                    JumpCurrentViewer();
                    e.Handled = true;
                    break;
                case Key.Left:
                case Key.PageUp:
                    if (_session != null && _viewerIndex > 0)
                    {
                        _viewerIndex--;
                        ShowViewer();
                    }
                    e.Handled = true;
                    break;
                case Key.Right:
                case Key.PageDown:
                    if (_session != null && _viewerIndex + 1 < _session.Book.Illustrations.Count)
                    {
                        _viewerIndex++;
                        ShowViewer();
                    }
                    e.Handled = true;
                    break;
            }
            return;
        }

        if (e.Key == Key.O && (Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            OnOpen(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }
        if (e.Key == Key.O && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            OnOpen(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (_session == null)
        {
            if (e.Key == Key.Escape) HideOverlays();
            return;
        }

        switch (e.Key)
        {
            case Key.PageDown:
            case Key.Space:
                Turn(true); e.Handled = true; break;
            case Key.PageUp:
                Turn(false); e.Handled = true; break;
            case Key.Home:
                _session.PrevChapter(); Present(); e.Handled = true; break;
            case Key.End:
                _session.NextChapter(); Present(); e.Handled = true; break;
            case Key.Left:
                Turn(!VisualLtr()); e.Handled = true; break;
            case Key.Right:
                Turn(VisualLtr()); e.Handled = true; break;
            case Key.V:
                OnToggleVertical(this, new RoutedEventArgs()); e.Handled = true; break;
            case Key.P:
                OnToggleSpread(this, new RoutedEventArgs()); e.Handled = true; break;
            case Key.I:
                OnToggleIllust(this, new RoutedEventArgs()); e.Handled = true; break;
            case Key.T:
                OnToggleToc(this, new RoutedEventArgs()); e.Handled = true; break;
            case Key.C:
            case Key.F8:
                ToggleMenu(); e.Handled = true; break;
            case Key.OemPlus:
            case Key.Add:
                OnFontUp(this, new RoutedEventArgs()); e.Handled = true; break;
            case Key.OemMinus:
            case Key.Subtract:
                OnFontDown(this, new RoutedEventArgs()); e.Handled = true; break;
            case Key.F11:
                ToggleFullscreen(); e.Handled = true; break;
            case Key.Escape:
                HideOverlays(); e.Handled = true; break;
        }
    }

    void ToggleFullscreen()
    {
        if (WindowStyle == WindowStyle.None)
            ExitFullscreen();
        else
        {
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
        }
    }

    void ExitFullscreen()
    {
        WindowStyle = WindowStyle.SingleBorderWindow;
        WindowState = WindowState.Normal;
    }

    void SaveCurrent()
    {
        if (_session == null) return;
        ProgressStore.Remember(_state, _session.Book, _session.Snapshot());
        ProgressStore.Save(_state);
    }

    void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveCurrent();
        _session?.Dispose();
        _render?.Dispose();
    }

    sealed record TocRow(string Title, int SpineIndex)
    {
        public override string ToString() => Title;
    }
}
