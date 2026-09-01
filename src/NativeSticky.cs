using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace DailyStickyNative
{
    [DataContract]
    public class TaskItem
    {
        [DataMember(Name = "id")] public string Id { get; set; }
        [DataMember(Name = "text")] public string Text { get; set; }
        [DataMember(Name = "status")] public string Status { get; set; }
        [DataMember(Name = "createdAt")] public string CreatedAt { get; set; }
        [DataMember(Name = "carriedFrom", EmitDefaultValue = false)] public string CarriedFrom { get; set; }
        [DataMember(Name = "note", EmitDefaultValue = false)] public string Note { get; set; }
        [DataMember(Name = "deadline", EmitDefaultValue = false)] public string Deadline { get; set; }
    }

    [DataContract]
    public class StickySettings
    {
        [DataMember(Name = "alwaysOnTop")] public bool AlwaysOnTop { get; set; }
        [DataMember(Name = "locked")] public bool Locked { get; set; }
        [DataMember(Name = "opacity")] public double Opacity { get; set; }
        [DataMember(Name = "launchAtLogin")] public bool LaunchAtLogin { get; set; }
        [DataMember(Name = "textTone")] public int TextTone { get; set; }
        [DataMember(Name = "glassOpacity")] public double GlassOpacity { get; set; }
        [DataMember(Name = "titleText")] public string TitleText { get; set; }
        [DataMember(Name = "mode")] public string Mode { get; set; }
        [DataMember(Name = "textContent")] public string TextContent { get; set; }
    }

    [DataContract]
    public class WindowData
    {
        [DataMember(Name = "x", EmitDefaultValue = false)] public double X { get; set; }
        [DataMember(Name = "y", EmitDefaultValue = false)] public double Y { get; set; }
        [DataMember(Name = "width")] public double Width { get; set; }
        [DataMember(Name = "height")] public double Height { get; set; }
    }

    [DataContract]
    public class StickyStore
    {
        [DataMember(Name = "version")] public int Version { get; set; }
        [DataMember(Name = "settings")] public StickySettings Settings { get; set; }
        [DataMember(Name = "window")] public WindowData Window { get; set; }
        [DataMember(Name = "days")] public Dictionary<string, List<TaskItem>> Days { get; set; }
        [DataMember(Name = "open")] public bool Open { get; set; }
    }

    public class DailyStickyWindow : Window
    {
        private readonly Color Ink = Colors.White;
        private readonly Color Muted = Colors.White;
        private readonly Color Red = Color.FromRgb(255, 95, 87);
        private readonly Color Green = Color.FromRgb(40, 200, 96);
        private readonly Color Blue = Color.FromRgb(74, 151, 255);
        private StickyStore store;
        private string selectedDate;
        private string observedToday;
        private StackPanel taskPanel;
        private TextBlock dateText;
        private TextBlock countText;
        private Button nextButton;
        private Border shell;
        private DispatcherTimer rolloverTimer;
        private DispatcherTimer scrollbarHideTimer;
        private ScrollViewer taskScroll;
        private ScrollBar taskVerticalBar;
        private int scrollbarFadeVersion;
        private bool syncingScrollBar;
        private bool isDraggingTask;
        private TextBox textContentEditor;
        private readonly string storageId;
        private readonly string requestedMode;
        private static int extraWindowCount;

        public DailyStickyWindow() : this("main", null) { }

        public DailyStickyWindow(string id, string initialMode)
        {
            storageId = string.IsNullOrWhiteSpace(id) ? "main" : id;
            requestedMode = initialMode;
            store = LoadStore();
            if (storageId != "main" && !File.Exists(DataPath())) store.Open = true;
            if (storageId == "main" && store.Settings.LaunchAtLogin && !SetStartup(true)) store.Settings.LaunchAtLogin = false;
            if (!string.IsNullOrWhiteSpace(initialMode) && string.IsNullOrWhiteSpace(store.Settings.Mode)) store.Settings.Mode = initialMode;
            observedToday = DateKey(DateTime.Now);
            selectedDate = observedToday;
            if (!IsTextMode) EnsureToday();

            Width = Clamp(store.Window.Width, 350, 900, 390);
            Height = Clamp(store.Window.Height, 430, 1000, 540);
            MinWidth = 350;
            MinHeight = 430;
            if (store.Window.X != 0 || store.Window.Y != 0)
            {
                Left = store.Window.X;
                Top = store.Window.Y;
                WindowStartupLocation = WindowStartupLocation.Manual;
            }
            else if (storageId != "main")
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = 90 + (extraWindowCount % 6) * 32;
                Top = 90 + (extraWindowCount % 6) * 28;
                extraWindowCount++;
            }
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;

            Title = string.IsNullOrWhiteSpace(store.Settings.TitleText) ? "每日便签" : store.Settings.TitleText;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
            ShowInTaskbar = false;
            Background = Brushes.Transparent;
            Topmost = store.Settings.AlwaysOnTop;
            // Keep text and controls fully opaque; glass strength is controlled by the backdrop tint instead.
            Opacity = 1;
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(7),
                CornerRadius = new CornerRadius(0),
                // Win10 needs the DWM frame extended through the whole client area
                // so the acrylic layer can sample and blur the desktop behind it.
                GlassFrameThickness = new Thickness(0),
                UseAeroCaptionButtons = false
            });

            BuildInterface();
            PreviewMouseDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (!IsInsideTextBox(e.OriginalSource as DependencyObject)) Keyboard.ClearFocus();
            };
            SourceInitialized += delegate { ApplyAcrylic(); ScheduleRoundedWindowRegion(); };
            ContentRendered += delegate { ScheduleRoundedWindowRegion(); };
            Activated += delegate { ScheduleRoundedWindowRegion(); };
            Deactivated += delegate { ScheduleRoundedWindowRegion(); };
            Closing += delegate { CommitFocusedEditor(); if (storageId != "main" && !Program.Exiting) store.Open = false; SaveStore(); };
            LocationChanged += delegate { RememberBounds(); };
            SizeChanged += delegate { RememberBounds(); ScheduleRoundedWindowRegion(); };

            rolloverTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            rolloverTimer.Tick += CheckDateRollover;
            rolloverTimer.Start();
        }

        private void BuildInterface()
        {
            CommitTextContent();
            shell = new Border
            {
                CornerRadius = new CornerRadius(22),
                BorderThickness = new Thickness(1.35),
                BorderBrush = CreateEdgeBrush(),
                Background = CreateGlassBrush()
            };
            Content = shell;

            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
            if (IsTextMode)
            {
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
            }
            else
            {
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
            }
            shell.Child = root;
            shell.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (!IsTextMode || store.Settings.Locked) return;
                DependencyObject source = e.OriginalSource as DependencyObject;
                if (IsInsideTextBox(source) || IsInsideButton(source) || IsInsideScrollBar(source)) return;
                e.Handled = true;
                DragMove();
            };

            Border grain = new Border
            {
                CornerRadius = new CornerRadius(21),
                Background = CreateFrostGrainBrush(),
                Opacity = .055,
                IsHitTestVisible = false,
                Focusable = false
            };
            Grid.SetRowSpan(grain, IsTextMode ? 3 : 4);
            Panel.SetZIndex(grain, 0);
            root.Children.Add(grain);

            root.Children.Add(BuildTitleBar());
            if (IsTextMode)
            {
                Grid contentGrid = new Grid { Margin = new Thickness(12, 8, 12, 5) };
                contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
                contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                contentGrid.Children.Add(new TextBlock { Text = "内容", Foreground = ThemeBrush(), Opacity = .62, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
                textContentEditor = new TextBox
                {
                    Text = store.Settings.TextContent ?? "",
                    Foreground = ThemeBrush(), CaretBrush = ThemeBrush(),
                    Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                    FontSize = 13, Padding = new Thickness(10, 8, 10, 8),
                    TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    MaxLength = 10000
                };
                textContentEditor.LostKeyboardFocus += delegate { CommitTextContent(); };
                Border contentSurface = new Border
                {
                    CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(52, 255, 255, 255)),
                    Background = new SolidColorBrush(Color.FromArgb(12, 255, 255, 255)), Child = textContentEditor
                };
                Grid.SetRow(contentSurface, 1);
                contentGrid.Children.Add(contentSurface);
                Grid.SetRow(contentGrid, 1);
                root.Children.Add(contentGrid);
                FrameworkElement footerText = BuildFooter();
                Grid.SetRow(footerText, 2);
                root.Children.Add(footerText);
                return;
            }
            FrameworkElement dateBar = BuildDateBar();
            Grid.SetRow(dateBar, 1);
            root.Children.Add(dateBar);

            taskScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Margin = new Thickness(10, 0, 7, 0),
                Background = Brushes.Transparent,
                Focusable = false,
                IsTabStop = false,
                FocusVisualStyle = null
            };
            taskPanel = new StackPanel { Focusable = false };
            taskScroll.Content = taskPanel;
            taskScroll.Loaded += delegate { InitializeAutoHideScrollbar(); };
            taskScroll.ScrollChanged += delegate(object sender, ScrollChangedEventArgs e) { UpdateTaskScrollbar(); if (Math.Abs(e.VerticalChange) > 0.1) ShowTaskScrollbar(); };
            taskScroll.PreviewMouseWheel += delegate { ShowTaskScrollbar(); };
            taskScroll.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (e.GetPosition(taskScroll).X >= taskScroll.ActualWidth - 24) ShowTaskScrollbar();
            };
            Grid scrollHost = new Grid();
            scrollHost.Children.Add(taskScroll);
            taskVerticalBar = new ScrollBar
            {
                Orientation = Orientation.Vertical,
                Width = 0,
                Opacity = 0,
                IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(0, 2, 1, 2),
                Minimum = 0,
                SmallChange = 44
            };
            taskVerticalBar.ValueChanged += delegate
            {
                if (!syncingScrollBar && taskScroll != null) taskScroll.ScrollToVerticalOffset(taskVerticalBar.Value);
            };
            Panel.SetZIndex(taskVerticalBar, 10);
            scrollHost.Children.Add(taskVerticalBar);
            Grid.SetRow(scrollHost, 2);
            root.Children.Add(scrollHost);

            FrameworkElement footer = BuildFooter();
            Grid.SetRow(footer, 3);
            root.Children.Add(footer);
            RenderTasks();
        }

        private FrameworkElement BuildTitleBar()
        {
            Grid bar = new Grid { Background = Brushes.Transparent };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (IsInsideTextBox(e.OriginalSource as DependencyObject)) return;
                if (!store.Settings.Locked && e.ButtonState == MouseButtonState.Pressed) DragMove();
            };

            StackPanel brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            TextBox titleEditor = new TextBox
            {
                Text = string.IsNullOrWhiteSpace(store.Settings.TitleText) ? "每日便签" : store.Settings.TitleText,
                Foreground = ThemeBrush(),
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontSize = 14, FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(3, 0, 3, 0), Width = 170,
                VerticalContentAlignment = VerticalAlignment.Center, MaxLength = 30, ToolTip = "点击修改便签标题", Focusable = false
            };
            titleEditor.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (!titleEditor.Focusable)
                {
                    titleEditor.Focusable = true;
                    titleEditor.Focus();
                }
            };
            titleEditor.GotKeyboardFocus += delegate
            {
                titleEditor.Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));
                titleEditor.BorderBrush = new SolidColorBrush(Color.FromArgb(105, 255, 255, 255));
                titleEditor.BorderThickness = new Thickness(0, 0, 0, 1);
            };
            titleEditor.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) { e.Handled = true; SaveTitle(titleEditor); Keyboard.ClearFocus(); }
                else if (e.Key == Key.Escape) { e.Handled = true; titleEditor.Text = store.Settings.TitleText; Keyboard.ClearFocus(); }
            };
            titleEditor.LostKeyboardFocus += delegate
            {
                SaveTitle(titleEditor);
                titleEditor.Background = Brushes.Transparent;
                titleEditor.BorderBrush = Brushes.Transparent;
                titleEditor.BorderThickness = new Thickness(0);
                titleEditor.Focusable = false;
            };
            brand.Children.Add(titleEditor);
            bar.Children.Add(brand);

            StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 5, 0) };
            Button minimize = ChromeButton("—", "最小化");
            minimize.Click += delegate { WindowState = WindowState.Minimized; };
            Button close = ChromeButton("×", "关闭");
            close.Click += delegate { Close(); };
            actions.Children.Add(minimize);
            actions.Children.Add(close);
            Grid.SetColumn(actions, 1);
            bar.Children.Add(actions);
            return bar;
        }

        private FrameworkElement BuildDateBar()
        {
            Grid bar = new Grid { Margin = new Thickness(10, 0, 10, 0) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });

            Button previous = CompactButton("‹", "前一天");
            previous.Click += delegate { SetDate(ParseDate(selectedDate).AddDays(-1)); };
            bar.Children.Add(previous);

            dateText = new TextBlock { FontSize = 12, FontWeight = FontWeights.Bold, Foreground = ThemeBrush(), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            dateText.MouseLeftButtonDown += delegate { SetDate(DateTime.Now); };
            Grid.SetColumn(dateText, 1);
            bar.Children.Add(dateText);

            countText = new TextBlock { FontSize = 12, Foreground = ThemeBrush(), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(countText, 2);
            bar.Children.Add(countText);

            nextButton = CompactButton("›", "后一天");
            nextButton.Click += delegate { SetDate(ParseDate(selectedDate).AddDays(1)); };
            Grid.SetColumn(nextButton, 3);
            bar.Children.Add(nextButton);
            return bar;
        }

        private FrameworkElement BuildFooter()
        {
            Grid footer = new Grid { Margin = new Thickness(11, 0, 9, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Button palette = IconButton(PaletteGeometry(), "文字颜色与玻璃透明度");
            palette.Width = 30;
            palette.Height = 30;
            palette.Click += delegate
            {
                int originalTone = store.Settings.TextTone;
                double originalOpacity = store.Settings.GlassOpacity;
                AppearanceWindow dialog = new AppearanceWindow(originalTone, originalOpacity, PreviewAppearance);
                dialog.Owner = this;
                if (dialog.ShowDialog() == true)
                {
                    store.Settings.TextTone = dialog.SelectedTone;
                    store.Settings.GlassOpacity = dialog.SelectedOpacity;
                    SaveStore();
                }
                else PreviewAppearance(originalTone, originalOpacity);
            };
            Grid.SetColumn(palette, 1);
            footer.Children.Add(palette);
            Button settings = TextButton("设置");
            settings.ContextMenu = BuildSettingsMenu();
            settings.Click += delegate { settings.ContextMenu.PlacementTarget = settings; settings.ContextMenu.IsOpen = true; };
            Grid.SetColumn(settings, 2);
            footer.Children.Add(settings);
            return footer;
        }

        private ContextMenu BuildSettingsMenu()
        {
            ContextMenu menu = new ContextMenu();
            MenuItem top = new MenuItem { Header = "始终置顶", IsCheckable = true, IsChecked = store.Settings.AlwaysOnTop };
            top.Click += delegate { store.Settings.AlwaysOnTop = top.IsChecked; Topmost = top.IsChecked; SaveStore(); };
            MenuItem locked = new MenuItem { Header = "锁定位置", IsCheckable = true, IsChecked = store.Settings.Locked };
            locked.Click += delegate { store.Settings.Locked = locked.IsChecked; SaveStore(); };
            MenuItem startup = new MenuItem { Header = "开机启动", IsCheckable = true, IsChecked = store.Settings.LaunchAtLogin };
            startup.Click += delegate
            {
                bool requested = startup.IsChecked;
                if (SetStartup(requested)) store.Settings.LaunchAtLogin = requested;
                else
                {
                    startup.IsChecked = !requested;
                    store.Settings.LaunchAtLogin = !requested;
                    MessageBox.Show(this, "无法写入 Windows 开机启动项。请检查当前账户权限或在任务管理器的“启动”页面确认该程序未被禁用。", "开机启动设置失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                SaveStore();
            };
            MenuItem newTask = new MenuItem { Header = "新建任务便签" };
            newTask.Click += delegate { CreateAdditionalSticky("tasks"); };
            MenuItem newText = new MenuItem { Header = "新建文字便签" };
            newText.Click += delegate { CreateAdditionalSticky("text"); };
            MenuItem switchMode = new MenuItem { Header = IsTextMode ? "切换为任务便签" : "切换为文字便签" };
            switchMode.Click += delegate
            {
                CommitTextContent();
                store.Settings.Mode = IsTextMode ? "tasks" : "text";
                if (!IsTextMode) EnsureToday();
                SaveStore();
                BuildInterface();
            };
            menu.Items.Add(top);
            menu.Items.Add(locked);
            menu.Items.Add(startup);
            menu.Items.Add(new Separator());
            menu.Items.Add(newTask);
            menu.Items.Add(newText);
            menu.Items.Add(switchMode);
            menu.Items.Add(new Separator());
            menu.Items.Add(SizeItem("紧凑尺寸", 360, 460));
            menu.Items.Add(SizeItem("标准尺寸", 390, 540));
            menu.Items.Add(SizeItem("宽松尺寸", 460, 650));
            return menu;
        }

        private MenuItem SizeItem(string label, double width, double height)
        {
            MenuItem item = new MenuItem { Header = label };
            item.Click += delegate { Width = width; Height = height; RememberBounds(); SaveStore(); };
            return item;
        }

        private MenuItem OpacityItem(string label, double value)
        {
            MenuItem item = new MenuItem { Header = label };
            item.Click += delegate { SetGlassLevel(value); };
            return item;
        }

        private void RenderTasks()
        {
            taskPanel.Children.Clear();
            List<TaskItem> tasks = CurrentTasks();
            DateTime date = ParseDate(selectedDate);
            string weekday = new System.Globalization.CultureInfo("zh-CN").DateTimeFormat.GetDayName(date.DayOfWeek);
            dateText.Text = string.Format("{0}月{1}日 · {2}", date.Month, date.Day, weekday);
            int done = tasks.Count(t => t.Status == "done");
            countText.Text = string.Format("{0}/{1}", done, tasks.Count);
            nextButton.IsEnabled = selectedDate.CompareTo(DateKey(DateTime.Now)) < 0;

            if (tasks.Count == 0)
            {
                TextBlock empty = new TextBlock
                {
                    Text = selectedDate == DateKey(DateTime.Now) ? "点击下方＋，添加今天的第一件事" : "当日没有任务记录",
                    Foreground = ThemeBrush(), FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 55, 0, 0)
                };
                taskPanel.Children.Add(empty);
            }
            else
            {
                for (int index = 0; index < tasks.Count; index++) taskPanel.Children.Add(BuildTaskBlock(tasks[index], index));
            }

            Button addLast = IconButton(PlusGeometry(), "在列表末尾添加任务");
            addLast.Width = 44;
            addLast.Height = 38;
            addLast.Margin = new Thickness(0, 9, 0, 9);
            addLast.HorizontalAlignment = HorizontalAlignment.Center;
            addLast.Opacity = .94;
            addLast.Click += delegate { AddTask(CurrentTasks().Count); };
            taskPanel.Children.Add(addLast);
        }

        private FrameworkElement BuildTaskBlock(TaskItem task, int index)
        {
            StackPanel block = new StackPanel { Tag = task.Id, AllowDrop = true };
            Grid row = new Grid { MinHeight = 44, Background = Brushes.Transparent };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(29) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(7) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });

            TextBlock number = new TextBlock
            {
                Text = (index + 1).ToString("00"), FontSize = 12,
                Foreground = task.Status == "done" ? new SolidColorBrush(Color.FromArgb(82, ThemeColor().R, ThemeColor().G, ThemeColor().B)) : ThemeBrush(),
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center
            };
            number.Tag = task;
            row.Children.Add(number);

            Grid editor = new Grid();
            editor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            editor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            TextBox input = new TextBox
            {
                Text = task.Text ?? "", Tag = task, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
                Foreground = task.Status == "done" ? new SolidColorBrush(Color.FromArgb(66, ThemeColor().R, ThemeColor().G, ThemeColor().B)) : ThemeBrush(),
                FontSize = 12, Padding = new Thickness(2, 10, 2, 8), VerticalContentAlignment = VerticalAlignment.Center,
                TextDecorations = null,
                MaxLength = 180
            };
            Button confirm = IconButton(CheckGeometry(), "确认编辑");
            confirm.Opacity = 0;
            input.GotKeyboardFocus += delegate { confirm.Opacity = 1; };
            input.TextChanged += delegate { confirm.Opacity = 1; };
            input.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) { e.Handled = true; ConfirmTask(input, confirm); }
            };
            confirm.Click += delegate { ConfirmTask(input, confirm); };
            editor.Children.Add(input);
            Grid.SetColumn(confirm, 1);
            editor.Children.Add(confirm);
            if (task.Status == "done")
            {
                Border strike = new Border
                {
                    Height = 1,
                    Background = new SolidColorBrush(Color.FromArgb(126, ThemeColor().R, ThemeColor().G, ThemeColor().B)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(2, 0, 3, 0),
                    IsHitTestVisible = false,
                    Tag = "completion-strike"
                };
                Grid.SetColumnSpan(strike, 2);
                Panel.SetZIndex(strike, 5);
                editor.Children.Add(strike);
            }
            Grid.SetColumn(editor, 1);
            row.Children.Add(editor);

            Button noteButton = IconButton(NoteGeometry(), "任务备注与时间要求");
            noteButton.Width = 27;
            noteButton.Height = 30;
            noteButton.Opacity = task.Status == "done" ? .24 : (string.IsNullOrWhiteSpace(task.Note) && string.IsNullOrWhiteSpace(task.Deadline) ? .42 : 1);
            noteButton.Click += delegate
            {
                CommitFocusedEditor();
                TaskNoteWindow dialog = new TaskNoteWindow(task, store.Settings.TextTone);
                dialog.Owner = this;
                if (dialog.ShowDialog() == true) { SaveStore(); RenderTasks(); }
            };
            Grid.SetColumn(noteButton, 2);
            row.Children.Add(noteButton);

            Border statusHost = new Border
            {
                Background = Brushes.Transparent, Padding = new Thickness(3), Cursor = Cursors.SizeAll,
                ToolTip = "拖动此区域调整顺序"
            };
            StackPanel balls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            balls.Children.Add(StatusBall(task, "todo", Red, "未完成"));
            balls.Children.Add(StatusBall(task, "doing", Green, "本日需完成"));
            balls.Children.Add(StatusBall(task, "done", Blue, "已完成"));
            statusHost.Child = balls;
            statusHost.PreviewMouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (IsInsideButton(e.OriginalSource as DependencyObject)) return;
                if (!isDraggingTask && e.LeftButton == MouseButtonState.Pressed)
                {
                    isDraggingTask = true;
                    DragDrop.DoDragDrop(statusHost, task.Id, DragDropEffects.Move);
                    isDraggingTask = false;
                }
            };
            Grid.SetColumn(statusHost, 4);
            row.Children.Add(statusHost);

            ContextMenu context = new ContextMenu();
            MenuItem remove = new MenuItem { Header = "删除这条任务" };
            remove.Click += delegate { CurrentTasks().Remove(task); SaveStore(); RenderTasks(); };
            context.Items.Add(remove);
            row.ContextMenu = context;
            Border taskSurface = new Border
            {
                Child = row,
                CornerRadius = new CornerRadius(0),
                BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(98, 255, 255, 255)),
                Background = Brushes.Transparent,
                Margin = new Thickness(3, 0, 3, 0)
            };
            block.Children.Add(taskSurface);
            block.DragOver += delegate(object sender, DragEventArgs e) { e.Effects = DragDropEffects.Move; e.Handled = true; };
            block.Drop += delegate(object sender, DragEventArgs e) { Reorder((string)e.Data.GetData(typeof(string)), task.Id); };
            return block;
        }

        private Button StatusBall(TaskItem task, string status, Color color, string label)
        {
            LinearGradientBrush fill = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(1, 1)
            };
            fill.GradientStops.Add(new GradientStop(Lighten(color, .06), 0));
            fill.GradientStops.Add(new GradientStop(color, .55));
            fill.GradientStops.Add(new GradientStop(Darken(color, .10), 1));
            Ellipse orb = new Ellipse
            {
                Width = 15, Height = 15, Fill = fill,
                Stroke = new SolidColorBrush(task.Status == status ? Color.FromArgb(158, 255, 255, 255) : Colors.Transparent),
                StrokeThickness = task.Status == status ? .65 : 0,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = task.Status == status ? color : Color.FromRgb(48, 61, 65),
                    BlurRadius = task.Status == status ? 4 : 3,
                    ShadowDepth = task.Status == status ? .5 : 1,
                    Opacity = task.Status == status ? .38 : .04
                },
                RenderTransformOrigin = new Point(.5, .5),
                RenderTransform = new ScaleTransform(task.Status == status ? 1.04 : .96, task.Status == status ? 1.04 : .96),
                Opacity = task.Status == status ? 1 : .20
            };
            Button button = new Button
            {
                Content = orb, Width = 22, Height = 28, Padding = new Thickness(0), Margin = new Thickness(1, 0, 1, 0),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), ToolTip = label, Cursor = Cursors.Hand
            };
            ApplyFlatButtonTemplate(button);
            button.Click += delegate
            {
                task.Status = status;
                ScaleTransform scale = (ScaleTransform)orb.RenderTransform;
                DoubleAnimation pulse = new DoubleAnimation(.84, 1.10, TimeSpan.FromMilliseconds(180)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                pulse.Completed += delegate { SaveStore(); RenderTasks(); };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
            };
            return button;
        }

        private void ConfirmTask(TextBox input, Button confirm)
        {
            TaskItem task = (TaskItem)input.Tag;
            task.Text = (input.Text ?? "").Trim();
            input.Text = task.Text;
            input.CaretIndex = 0;
            input.ScrollToHorizontalOffset(0);
            confirm.Opacity = 0;
            Keyboard.ClearFocus();
            SaveStore();
        }

        private void AddTask(int index)
        {
            TaskItem task = new TaskItem { Id = Guid.NewGuid().ToString("N"), Text = "", Status = "todo", CreatedAt = DateTime.UtcNow.ToString("o") };
            List<TaskItem> tasks = CurrentTasks();
            tasks.Insert(Math.Max(0, Math.Min(index, tasks.Count)), task);
            SaveStore();
            RenderTasks();
            Dispatcher.BeginInvoke(new Action(delegate
            {
                TextBox box = FindEditor(taskPanel, task.Id);
                if (box != null) { box.Focus(); box.SelectAll(); }
            }), DispatcherPriority.Loaded);
        }

        private TextBox FindEditor(DependencyObject parent, string id)
        {
            if (parent is TextBox && ((TextBox)parent).Tag is TaskItem && ((TaskItem)((TextBox)parent).Tag).Id == id) return (TextBox)parent;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                TextBox found = FindEditor(VisualTreeHelper.GetChild(parent, i), id);
                if (found != null) return found;
            }
            return null;
        }

        private bool IsInsideButton(DependencyObject source)
        {
            DependencyObject current = source;
            while (current != null)
            {
                if (current is Button) return true;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        private bool IsInsideTextBox(DependencyObject source)
        {
            DependencyObject current = source;
            while (current != null)
            {
                if (current is TextBox) return true;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        private bool IsInsideScrollBar(DependencyObject source)
        {
            DependencyObject current = source;
            while (current != null)
            {
                if (current is ScrollBar) return true;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        private void SaveTitle(TextBox editor)
        {
            string value = (editor.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value)) value = "每日便签";
            editor.Text = value;
            if (store.Settings.TitleText == value) return;
            store.Settings.TitleText = value;
            Title = value;
            SaveStore();
        }

        private void Reorder(string movingId, string targetId)
        {
            if (string.IsNullOrEmpty(movingId) || movingId == targetId) return;
            List<TaskItem> tasks = CurrentTasks();
            TaskItem moving = tasks.FirstOrDefault(t => t.Id == movingId);
            TaskItem target = tasks.FirstOrDefault(t => t.Id == targetId);
            if (moving == null || target == null) return;
            tasks.Remove(moving);
            tasks.Insert(tasks.IndexOf(target), moving);
            SaveStore();
            RenderTasks();
        }

        private List<TaskItem> CurrentTasks()
        {
            if (!store.Days.ContainsKey(selectedDate)) store.Days[selectedDate] = new List<TaskItem>();
            return store.Days[selectedDate];
        }

        private void SetDate(DateTime date)
        {
            DateTime today = DateTime.Today;
            DateTime earliest = today.AddDays(-29);
            if (date > today) date = today;
            if (date < earliest) date = earliest;
            CommitFocusedEditor();
            selectedDate = DateKey(date);
            RenderTasks();
        }

        private void CheckDateRollover(object sender, EventArgs e)
        {
            string now = DateKey(DateTime.Now);
            if (now == observedToday) return;
            bool followedToday = selectedDate == observedToday;
            observedToday = now;
            EnsureToday();
            if (followedToday) selectedDate = now;
            SaveStore();
            RenderTasks();
        }

        private void EnsureToday()
        {
            string today = DateKey(DateTime.Now);
            if (!store.Days.ContainsKey(today))
            {
                string prior = store.Days.Keys.Where(k => string.CompareOrdinal(k, today) < 0).OrderByDescending(k => k).FirstOrDefault();
                List<TaskItem> inherited = new List<TaskItem>();
                if (prior != null)
                {
                    foreach (TaskItem item in store.Days[prior].Where(t => t.Status != "done"))
                    {
                        inherited.Add(new TaskItem
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            Text = item.Text,
                            Status = item.Status == "doing" ? "todo" : item.Status,
                            CreatedAt = DateTime.UtcNow.ToString("o"),
                            CarriedFrom = prior,
                            Note = item.Note,
                            Deadline = item.Deadline
                        });
                    }
                }
                store.Days[today] = inherited;
            }
            string cutoff = DateKey(DateTime.Today.AddDays(-29));
            foreach (string key in store.Days.Keys.Where(k => string.CompareOrdinal(k, cutoff) < 0).ToList()) store.Days.Remove(key);
        }

        private StickyStore LoadStore()
        {
            StickyStore fallback = DefaultStore();
            string path = DataPath();
            if (!File.Exists(path)) return fallback;
            try
            {
                using (FileStream stream = File.OpenRead(path))
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(StickyStore), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
                    StickyStore loaded = (StickyStore)serializer.ReadObject(stream);
                    if (loaded == null || loaded.Days == null) throw new SerializationException("Invalid data");
                    loaded.Settings = loaded.Settings ?? fallback.Settings;
                    loaded.Window = loaded.Window ?? fallback.Window;
                    if (loaded.Settings.Opacity <= 0) loaded.Settings.Opacity = .94;
                    if (loaded.Version < 2)
                    {
                        loaded.Settings.TextTone = 5;
                        loaded.Settings.GlassOpacity = .21;
                        loaded.Version = 2;
                    }
                    if (loaded.Version < 3)
                    {
                        loaded.Settings.TitleText = "每日便签";
                        loaded.Version = 3;
                    }
                    if (loaded.Version < 4)
                    {
                        loaded.Settings.Mode = "tasks";
                        loaded.Settings.TextContent = loaded.Settings.TextContent ?? "";
                        loaded.Version = 4;
                    }
                    if (string.IsNullOrWhiteSpace(loaded.Settings.TitleText)) loaded.Settings.TitleText = "每日便签";
                    if (string.IsNullOrWhiteSpace(loaded.Settings.Mode)) loaded.Settings.Mode = "tasks";
                    loaded.Settings.TextTone = Math.Max(0, Math.Min(5, loaded.Settings.TextTone));
                    loaded.Settings.GlassOpacity = Clamp(loaded.Settings.GlassOpacity, .10, 1, .21);
                    return loaded;
                }
            }
            catch
            {
                try { File.Copy(path, path + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss"), true); } catch { }
                return fallback;
            }
        }

        private void SaveStore()
        {
            try
            {
                RememberBounds();
                string path = DataPath();
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                string temp = path + ".tmp";
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(StickyStore), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
                using (FileStream stream = File.Create(temp)) serializer.WriteObject(stream, store);
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak", true);
                else File.Move(temp, path);
            }
            catch { }
        }

        private void RememberBounds()
        {
            if (store == null || store.Window == null || WindowState != WindowState.Normal) return;
            store.Window.X = Left; store.Window.Y = Top; store.Window.Width = Width; store.Window.Height = Height;
        }

        private void CommitFocusedEditor()
        {
            TextBox input = Keyboard.FocusedElement as TextBox;
            if (input != null && input.Tag is TaskItem)
            {
                ((TaskItem)input.Tag).Text = (input.Text ?? "").Trim();
                input.CaretIndex = 0;
                input.ScrollToHorizontalOffset(0);
            }
            CommitTextContent();
        }

        private void CommitTextContent()
        {
            if (textContentEditor != null && IsTextMode)
            {
                string value = textContentEditor.Text ?? "";
                if (store.Settings.TextContent != value)
                {
                    store.Settings.TextContent = value;
                    SaveStore();
                }
            }
        }

        private bool IsTextMode { get { return string.Equals(store.Settings.Mode, "text", StringComparison.OrdinalIgnoreCase); } }
        public bool ShouldRestore { get { return storageId != "main" && store.Open; } }

        private void CreateAdditionalSticky(string mode)
        {
            string id = Guid.NewGuid().ToString("N");
            DailyStickyWindow window = new DailyStickyWindow(id, mode);
            window.Show();
        }

        private StickyStore DefaultStore()
        {
            return new StickyStore
            {
                Version = 4,
                Settings = new StickySettings { Opacity = .94, TextTone = 5, GlassOpacity = .21, TitleText = requestedMode == "text" ? "文字便签" : "每日便签", Mode = requestedMode == "text" ? "text" : "tasks", TextContent = "" },
                Window = new WindowData { Width = 390, Height = 540 },
                Days = new Dictionary<string, List<TaskItem>>()
            };
        }

        private string DataPath()
        {
            string overrideDirectory = Environment.GetEnvironmentVariable("DAILY_STICKY_DATA_DIR");
            string directory = string.IsNullOrWhiteSpace(overrideDirectory)
                ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "daily-sticky")
                : overrideDirectory;
            string file = storageId == "main" ? "daily-sticky-data.json" : "daily-sticky-" + storageId + ".json";
            return System.IO.Path.Combine(directory, file);
        }
        private string DateKey(DateTime date) { return date.ToString("yyyy-MM-dd"); }
        private DateTime ParseDate(string key) { return DateTime.ParseExact(key, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture); }
        private double Clamp(double value, double min, double max, double fallback) { if (double.IsNaN(value) || value == 0) value = fallback; return Math.Max(min, Math.Min(max, value)); }
        private Color ThemeColor()
        {
            int selected = Math.Max(0, Math.Min(5, store.Settings.TextTone)) * 51;
            // A nearly opaque white glass layer and white text have no usable
            // contrast on bright desktops. Keep the chosen light tone at normal
            // glass levels, then smoothly darken only the rendered text as the
            // white veil becomes dense. No outline or shadow is added.
            if (store.Settings.TextTone >= 4 && store.Settings.GlassOpacity > .55)
            {
                double mix = Math.Min(1, (store.Settings.GlassOpacity - .55) / .45);
                selected = (int)Math.Round(selected + (55 - selected) * mix);
            }
            byte tone = (byte)Math.Max(0, Math.Min(255, selected));
            return Color.FromRgb(tone, tone, tone);
        }
        private Brush ThemeBrush() { return new SolidColorBrush(ThemeColor()); }

        private void PreviewAppearance(int tone, double opacity)
        {
            store.Settings.TextTone = Math.Max(0, Math.Min(5, tone));
            store.Settings.GlassOpacity = Clamp(opacity, .10, 1, .21);
            if (shell != null)
            {
                shell.Background = CreateGlassBrush();
                ApplyTextTone(shell);
            }
        }

        private void ApplyTextTone(DependencyObject root)
        {
            Color tone = ThemeColor();
            Brush normal = new SolidColorBrush(tone);
            TextBox box = root as TextBox;
            if (box != null)
            {
                TaskItem item = box.Tag as TaskItem;
                box.Foreground = item != null && item.Status == "done" ? new SolidColorBrush(Color.FromArgb(66, tone.R, tone.G, tone.B)) : normal;
                box.CaretBrush = normal;
            }
            else if (root is TextBlock)
            {
                TextBlock text = (TextBlock)root;
                TaskItem item = text.Tag as TaskItem;
                text.Foreground = item != null && item.Status == "done" ? new SolidColorBrush(Color.FromArgb(82, tone.R, tone.G, tone.B)) : normal;
            }
            else if (root is Button) ((Button)root).Foreground = normal;
            else if (root is System.Windows.Shapes.Path) ((System.Windows.Shapes.Path)root).Stroke = normal;
            else if (root is Border && object.Equals(((Border)root).Tag, "completion-strike"))
                ((Border)root).Background = new SolidColorBrush(Color.FromArgb(126, tone.R, tone.G, tone.B));

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) ApplyTextTone(VisualTreeHelper.GetChild(root, i));
        }

        private void InitializeAutoHideScrollbar()
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (taskVerticalBar == null) return;
                taskVerticalBar.Opacity = 0;
                taskVerticalBar.Width = 0;
                taskVerticalBar.IsHitTestVisible = false;
                UpdateTaskScrollbar();
                scrollbarHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                scrollbarHideTimer.Tick += delegate { scrollbarHideTimer.Stop(); HideTaskScrollbar(); };
            }), DispatcherPriority.Loaded);
        }

        private void UpdateTaskScrollbar()
        {
            if (taskVerticalBar == null || taskScroll == null) return;
            syncingScrollBar = true;
            taskVerticalBar.Maximum = Math.Max(0, taskScroll.ExtentHeight - taskScroll.ViewportHeight);
            taskVerticalBar.ViewportSize = taskScroll.ViewportHeight;
            taskVerticalBar.LargeChange = Math.Max(1, taskScroll.ViewportHeight);
            taskVerticalBar.Value = Math.Max(0, Math.Min(taskVerticalBar.Maximum, taskScroll.VerticalOffset));
            syncingScrollBar = false;
        }

        private ScrollBar FindOwnedVerticalScrollbar(DependencyObject parent)
        {
            if (parent == null) return null;
            ScrollBar bar = parent as ScrollBar;
            if (bar != null && bar.Orientation == Orientation.Vertical && object.ReferenceEquals(bar.TemplatedParent, taskScroll)) return bar;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                ScrollBar found = FindOwnedVerticalScrollbar(VisualTreeHelper.GetChild(parent, i));
                if (found != null) return found;
            }
            return null;
        }

        private void ShowTaskScrollbar()
        {
            if (taskVerticalBar == null || taskScroll == null || taskScroll.ScrollableHeight <= 0) return;
            scrollbarFadeVersion++;
            taskVerticalBar.Width = 12;
            taskVerticalBar.IsHitTestVisible = true;
            taskVerticalBar.BeginAnimation(UIElement.OpacityProperty, null);
            taskVerticalBar.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(taskVerticalBar.Opacity, 1, TimeSpan.FromMilliseconds(110)));
            if (scrollbarHideTimer != null) { scrollbarHideTimer.Stop(); scrollbarHideTimer.Start(); }
        }

        private void HideTaskScrollbar()
        {
            if (taskVerticalBar == null) return;
            int version = ++scrollbarFadeVersion;
            DoubleAnimation fade = new DoubleAnimation(taskVerticalBar.Opacity, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            fade.Completed += delegate
            {
                if (version == scrollbarFadeVersion && taskVerticalBar != null)
                {
                    taskVerticalBar.IsHitTestVisible = false;
                    taskVerticalBar.Width = 0;
                }
            };
            taskVerticalBar.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        private Button ChromeButton(string text, string tip)
        {
            Button button = new Button { Content = text, ToolTip = tip, Width = 29, Height = 25, FontSize = 12, Foreground = ThemeBrush(), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
            ApplyFlatButtonTemplate(button);
            return button;
        }

        private Button CompactButton(string text, string tip)
        {
            Button button = new Button { Content = text, ToolTip = tip, FontSize = 18, Foreground = ThemeBrush(), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
            ApplyFlatButtonTemplate(button);
            return button;
        }

        private Button TextButton(string text)
        {
            Button button = new Button { Content = text, FontSize = 12, Foreground = ThemeBrush(), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(6, 4, 6, 4), Cursor = Cursors.Hand };
            ApplyFlatButtonTemplate(button);
            return button;
        }

        private Button IconButton(Geometry geometry, string tip)
        {
            System.Windows.Shapes.Path path = new System.Windows.Shapes.Path { Data = geometry, Stroke = ThemeBrush(), StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            Button button = new Button { Content = path, ToolTip = tip, Width = 28, Height = 28, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
            ApplyFlatButtonTemplate(button);
            return button;
        }

        private void ApplyFlatButtonTemplate(Button button)
        {
            ControlTemplate template = new ControlTemplate(typeof(Button));
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);
            template.VisualTree = border;
            button.Template = template;
            button.FocusVisualStyle = null;
        }

        private Geometry CheckGeometry() { return Geometry.Parse("M 3,8 L 7,12 L 14,4"); }
        private Geometry PlusGeometry() { return Geometry.Parse("M 9,1.5 L 9,16.5 M 1.5,9 L 16.5,9"); }
        private Geometry NoteGeometry() { return Geometry.Parse("M 3.5,2.5 L 11,2.5 L 14.5,6 L 14.5,14.5 L 3.5,14.5 Z M 11,2.5 L 11,6 L 14.5,6 M 6,9 L 12,9 M 6,11.5 L 11,11.5"); }
        private Geometry PaletteGeometry() { return Geometry.Parse("M 9,2 C 4.8,2 2,4.9 2,8.4 C 2,12.1 5.1,15 8.7,15 C 9.9,15 10.5,14.3 10.3,13.4 C 10.1,12.5 10.8,11.8 11.8,11.9 C 14.3,12.2 16,10.5 16,8.2 C 16,4.8 13,2 9,2 Z M 5.1,7.2 L 5.1,7.2 M 7.2,4.8 L 7.2,4.8 M 10.3,4.6 L 10.3,4.6 M 13,6.6 L 13,6.6"); }
        private Color Lighten(Color color, double amount) { return Color.FromRgb((byte)(color.R + (255 - color.R) * amount), (byte)(color.G + (255 - color.G) * amount), (byte)(color.B + (255 - color.B) * amount)); }
        private Color Darken(Color color, double amount) { return Color.FromRgb((byte)(color.R * (1 - amount)), (byte)(color.G * (1 - amount)), (byte)(color.B * (1 - amount))); }

        private Brush CreateGlassBrush()
        {
            byte glassAlpha = (byte)Math.Round(255 * Clamp(store.Settings.GlassOpacity, .10, 1, .21));
            return new SolidColorBrush(Color.FromArgb(glassAlpha, 255, 255, 255));
        }

        private Brush CreateEdgeBrush()
        {
            LinearGradientBrush brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(238, 255, 255, 255), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(116, 255, 255, 255), .42));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(62, 20, 27, 30), .72));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(210, 5, 10, 12), 1));
            return brush;
        }

        private Brush CreateFrostGrainBrush()
        {
            const int size = 64;
            byte[] pixels = new byte[size * size * 4];
            Random random = new Random(7319);
            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte tone = (byte)random.Next(40, 216);
                pixels[i] = tone;
                pixels[i + 1] = tone;
                pixels[i + 2] = tone;
                pixels[i + 3] = 255;
            }

            WriteableBitmap texture = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
            texture.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
            texture.Freeze();
            ImageBrush brush = new ImageBrush(texture)
            {
                TileMode = TileMode.Tile,
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, size, size),
                Stretch = Stretch.None
            };
            brush.Freeze();
            return brush;
        }

        private void SetGlassLevel(double value)
        {
            store.Settings.Opacity = Clamp(value, .78, 1, .90);
            if (shell != null) shell.Background = CreateGlassBrush();
            ApplyAcrylic();
            SaveStore();
        }

        private bool SetStartup(bool enabled)
        {
            const string valueName = "DailySticky";
            string currentExecutable = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string installDirectory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DailyGlassNote");
            string installedExecutable = System.IO.Path.Combine(installDirectory, "DailyGlassNote.exe");
            string executable = currentExecutable;
            string startupDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            string shortcutPath = System.IO.Path.Combine(startupDirectory, "DailyGlassNote.lnk");
            try
            {
                if (enabled)
                {
                    try
                    {
                        Directory.CreateDirectory(installDirectory);
                        if (!string.Equals(currentExecutable, installedExecutable, StringComparison.OrdinalIgnoreCase))
                        {
                            File.Copy(currentExecutable, installedExecutable, true);
                            string sourceIcon = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(currentExecutable), "daily-note-badge-04.ico");
                            if (File.Exists(sourceIcon)) File.Copy(sourceIcon, System.IO.Path.Combine(installDirectory, "daily-note-badge-04.ico"), true);
                        }
                        executable = installedExecutable;
                    }
                    catch
                    {
                        if (File.Exists(installedExecutable)) executable = installedExecutable;
                    }

                    bool shortcutReady = false;
                    bool registryReady = false;
                    try
                    {
                        Directory.CreateDirectory(startupDirectory);
                        Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                        if (shellType != null)
                        {
                            object shell = Activator.CreateInstance(shellType);
                            object shortcut = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                            Type shortcutType = shortcut.GetType();
                            shortcutType.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { executable });
                            shortcutType.InvokeMember("Arguments", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { "--startup" });
                            shortcutType.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { System.IO.Path.GetDirectoryName(executable) });
                            shortcutType.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { "DailyGlassNote 每日便签" });
                            shortcutType.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { executable + ",0" });
                            shortcutType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);
                            if (Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
                            if (Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
                        }
                        shortcutReady = File.Exists(shortcutPath);
                    }
                    catch { }
                    try
                    {
                        using (RegistryKey key = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", RegistryKeyPermissionCheck.ReadWriteSubTree))
                        {
                            string command = "\"" + executable + "\" --startup";
                            if (key != null) { key.SetValue(valueName, command, RegistryValueKind.String); registryReady = string.Equals(Convert.ToString(key.GetValue(valueName, "")), command, StringComparison.OrdinalIgnoreCase); }
                        }
                    }
                    catch { }
                    return shortcutReady || registryReady;
                }
                try { if (File.Exists(shortcutPath)) File.Delete(shortcutPath); } catch { }
                try { using (RegistryKey key = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run")) if (key != null) key.DeleteValue(valueName, false); } catch { }
                return !File.Exists(shortcutPath);
            }
            catch { return false; }
        }

        [StructLayout(LayoutKind.Sequential)] private struct AccentPolicy { public int AccentState; public int AccentFlags; public int GradientColor; public int AnimationId; }
        [StructLayout(LayoutKind.Sequential)] private struct WindowCompositionAttributeData { public int Attribute; public IntPtr Data; public int SizeOfData; }
        [StructLayout(LayoutKind.Sequential)] private struct Margins { public int Left; public int Right; public int Top; public int Bottom; }
        [DllImport("user32.dll")] private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
        [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

        private void ApplyRoundedWindowRegion()
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero || ActualWidth < 2 || ActualHeight < 2) return;
            uint dpi = GetDpiForWindow(handle);
            double scale = dpi == 0 ? 1 : dpi / 96.0;
            int width = (int)Math.Ceiling(ActualWidth * scale);
            int height = (int)Math.Ceiling(ActualHeight * scale);
            int radius = (int)Math.Round(44 * scale);
            IntPtr region = CreateRoundRectRgn(0, 0, width + 1, height + 1, radius, radius);
            if (region == IntPtr.Zero) return;
            if (SetWindowRgn(handle, region, true) == 0) DeleteObject(region);
        }

        private void ScheduleRoundedWindowRegion()
        {
            ApplyRoundedWindowRegion();
            Dispatcher.BeginInvoke(new Action(ApplyRoundedWindowRegion), DispatcherPriority.ApplicationIdle);
            // DWM may recreate its blur surface a moment after activation/deactivation.
            // Re-apply the native region after those passes so the blur cannot spill
            // into the square corners when the window loses focus.
            int[] retries = new int[] { 40, 160, 420 };
            foreach (int delay in retries)
            {
                DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delay) };
                timer.Tick += delegate(object sender, EventArgs e)
                {
                    timer.Stop();
                    ApplyRoundedWindowRegion();
                };
                timer.Start();
            }
        }

        private void ApplyAcrylic()
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;

            HwndSource source = HwndSource.FromHwnd(handle);
            if (source != null && source.CompositionTarget != null)
                source.CompositionTarget.BackgroundColor = Colors.Transparent;

            int nonClientRenderingDisabled = 1;
            DwmSetWindowAttribute(handle, 2, ref nonClientRenderingDisabled, sizeof(int));

            double level = Clamp(store.Settings.Opacity, .78, 1, .94);
            double ratio = (level - .78) / .22;
            byte acrylicAlpha = (byte)(3 + ratio * 5);
            int glassColor = unchecked((int)(((uint)acrylicAlpha << 24) | 0x00FFFFFFu));
            // ACCENT_ENABLE_BLURBEHIND is more predictable than Acrylic on Win10;
            // the translucent WPF veil above supplies the white frosted tint.
            AccentPolicy policy = new AccentPolicy { AccentState = 3, AccentFlags = 2, GradientColor = glassColor, AnimationId = 0 };
            int size = Marshal.SizeOf(policy);
            IntPtr pointer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(policy, pointer, false);
                WindowCompositionAttributeData data = new WindowCompositionAttributeData { Attribute = 19, Data = pointer, SizeOfData = size };
                SetWindowCompositionAttribute(handle, ref data);
            }
            finally { Marshal.FreeHGlobal(pointer); }
            ApplyRoundedWindowRegion();
        }
    }

    public class AppearanceWindow : Window
    {
        public int SelectedTone { get; private set; }
        public double SelectedOpacity { get; private set; }
        private readonly List<Button> swatches = new List<Button>();
        private readonly Action<int, double> previewChanged;
        private TextBlock preview;
        private TextBlock percent;

        public AppearanceWindow(int tone, double opacity, Action<int, double> preview)
        {
            previewChanged = preview;
            SelectedTone = Math.Max(0, Math.Min(5, tone));
            SelectedOpacity = Math.Max(.10, Math.Min(1, opacity <= 0 ? .21 : opacity));
            Title = "外观设置";
            Width = 360;
            Height = 245;
            ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.None;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(0), CornerRadius = new CornerRadius(0), GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false });
            Content = BuildContent();
            SourceInitialized += delegate { ApplyBlur(); ScheduleRoundedWindowRegion(); };
            ContentRendered += delegate { ScheduleRoundedWindowRegion(); };
            Activated += delegate { ScheduleRoundedWindowRegion(); };
            Deactivated += delegate { ScheduleRoundedWindowRegion(); };
        }

        private FrameworkElement BuildContent()
        {
            LinearGradientBrush edge = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            edge.GradientStops.Add(new GradientStop(Color.FromArgb(235, 255, 255, 255), 0));
            edge.GradientStops.Add(new GradientStop(Color.FromArgb(70, 255, 255, 255), .5));
            edge.GradientStops.Add(new GradientStop(Color.FromArgb(210, 5, 10, 12), 1));
            Border shell = new Border { CornerRadius = new CornerRadius(20), BorderThickness = new Thickness(1.3), BorderBrush = edge, Background = new SolidColorBrush(Color.FromArgb(54, 255, 255, 255)) };
            Grid root = new Grid { Margin = new Thickness(18, 10, 18, 14) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(68) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(70) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });

            Grid title = new Grid { Background = Brushes.Transparent };
            title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            title.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
            title.Children.Add(new TextBlock { Text = "外观设置", Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
            Button close = FlatButton("×", 28);
            close.Click += delegate { DialogResult = false; };
            Grid.SetColumn(close, 1);
            title.Children.Add(close);
            root.Children.Add(title);

            StackPanel colorArea = new StackPanel();
            colorArea.Children.Add(new TextBlock { Text = "文字颜色", Foreground = Brushes.White, FontSize = 11, Opacity = .72, Margin = new Thickness(0, 4, 0, 7) });
            StackPanel swatchRow = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < 6; i++)
            {
                int index = i;
                byte shade = (byte)(i * 51);
                Ellipse circle = new Ellipse { Width = 22, Height = 22, Fill = new SolidColorBrush(Color.FromRgb(shade, shade, shade)) };
                Button swatch = new Button { Content = circle, Width = 42, Height = 30, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand, ToolTip = "灰度 " + (i + 1) };
                ApplyFlatTemplate(swatch);
                swatch.Click += delegate { SelectedTone = index; RefreshSwatches(); NotifyPreview(); };
                swatches.Add(swatch);
                swatchRow.Children.Add(swatch);
            }
            preview = new TextBlock { Text = "示例文字", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            swatchRow.Children.Add(preview);
            colorArea.Children.Add(swatchRow);
            Grid.SetRow(colorArea, 1);
            root.Children.Add(colorArea);

            Grid opacityArea = new Grid { Margin = new Thickness(0, 3, 0, 0) };
            opacityArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
            opacityArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
            opacityArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            opacityArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            opacityArea.Children.Add(new TextBlock { Text = "磨砂玻璃浓度", Foreground = Brushes.White, FontSize = 11, Opacity = .72, VerticalAlignment = VerticalAlignment.Center });
            percent = new TextBlock { Foreground = Brushes.White, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(percent, 1);
            opacityArea.Children.Add(percent);
            Slider slider = new Slider { Minimum = 10, Maximum = 100, Value = SelectedOpacity * 100, TickFrequency = 10, IsSnapToTickEnabled = false, VerticalAlignment = VerticalAlignment.Center };
            slider.ValueChanged += delegate { SelectedOpacity = slider.Value / 100.0; percent.Text = Math.Round(slider.Value) + "%"; NotifyPreview(); };
            Grid.SetRow(slider, 1);
            Grid.SetColumnSpan(slider, 2);
            opacityArea.Children.Add(slider);
            percent.Text = Math.Round(SelectedOpacity * 100) + "%";
            Grid.SetRow(opacityArea, 2);
            root.Children.Add(opacityArea);

            StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
            Button cancel = FlatButton("取消", 58);
            cancel.Opacity = .68;
            cancel.Click += delegate { DialogResult = false; };
            Button save = FlatButton("应用", 58);
            save.FontWeight = FontWeights.Bold;
            save.Click += delegate { DialogResult = true; };
            actions.Children.Add(cancel);
            actions.Children.Add(save);
            Grid.SetRow(actions, 3);
            root.Children.Add(actions);
            shell.Child = root;
            RefreshSwatches();
            return shell;
        }

        private void RefreshSwatches()
        {
            for (int i = 0; i < swatches.Count; i++)
            {
                Ellipse circle = swatches[i].Content as Ellipse;
                if (circle == null) continue;
                circle.Stroke = i == SelectedTone ? new SolidColorBrush(Color.FromRgb(74, 151, 255)) : new SolidColorBrush(Color.FromArgb(105, 255, 255, 255));
                circle.StrokeThickness = i == SelectedTone ? 2.2 : .7;
            }
            if (preview != null)
            {
                byte shade = (byte)(SelectedTone * 51);
                preview.Foreground = new SolidColorBrush(Color.FromRgb(shade, shade, shade));
            }
        }

        private void NotifyPreview()
        {
            if (previewChanged != null) previewChanged(SelectedTone, SelectedOpacity);
        }

        private Button FlatButton(string text, double width)
        {
            Button button = new Button { Content = text, Width = width, Height = 30, Foreground = Brushes.White, FontSize = 12, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
            ApplyFlatTemplate(button);
            return button;
        }

        private void ApplyFlatTemplate(Button button)
        {
            ControlTemplate template = new ControlTemplate(typeof(Button));
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);
            template.VisualTree = border;
            button.Template = template;
            button.FocusVisualStyle = null;
        }

        [StructLayout(LayoutKind.Sequential)] private struct AccentPolicy { public int AccentState; public int AccentFlags; public int GradientColor; public int AnimationId; }
        [StructLayout(LayoutKind.Sequential)] private struct CompositionData { public int Attribute; public IntPtr Data; public int SizeOfData; }
        [StructLayout(LayoutKind.Sequential)] private struct Margins { public int Left; public int Right; public int Top; public int Bottom; }
        [DllImport("user32.dll")] private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref CompositionData data);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
        [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

        private void ApplyBlur()
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            HwndSource source = HwndSource.FromHwnd(handle);
            if (source != null && source.CompositionTarget != null) source.CompositionTarget.BackgroundColor = Colors.Transparent;
            int nonClientRenderingDisabled = 1;
            DwmSetWindowAttribute(handle, 2, ref nonClientRenderingDisabled, sizeof(int));
            AccentPolicy policy = new AccentPolicy { AccentState = 3, AccentFlags = 2, GradientColor = unchecked((int)0x08FFFFFFu) };
            int size = Marshal.SizeOf(policy);
            IntPtr pointer = Marshal.AllocHGlobal(size);
            try { Marshal.StructureToPtr(policy, pointer, false); CompositionData data = new CompositionData { Attribute = 19, Data = pointer, SizeOfData = size }; SetWindowCompositionAttribute(handle, ref data); }
            finally { Marshal.FreeHGlobal(pointer); }
            ApplyRoundedWindowRegion();
        }

        private void ApplyRoundedWindowRegion()
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero || ActualWidth < 2 || ActualHeight < 2) return;
            double scale = GetDpiForWindow(handle) / 96.0;
            if (scale <= 0) scale = 1;
            IntPtr region = CreateRoundRectRgn(0, 0, (int)Math.Ceiling(ActualWidth * scale) + 1, (int)Math.Ceiling(ActualHeight * scale) + 1, (int)Math.Round(40 * scale), (int)Math.Round(40 * scale));
            if (region != IntPtr.Zero && SetWindowRgn(handle, region, true) == 0) DeleteObject(region);
        }

        private void ScheduleRoundedWindowRegion()
        {
            ApplyRoundedWindowRegion();
            Dispatcher.BeginInvoke(new Action(ApplyRoundedWindowRegion), DispatcherPriority.ApplicationIdle);
        }
    }

    public class TaskNoteWindow : Window
    {
        private readonly TaskItem task;
        private readonly Brush ink;
        private TextBox noteBox;
        private TextBox deadlineBox;

        public TaskNoteWindow(TaskItem source, int textTone)
        {
            task = source;
            byte tone = (byte)(Math.Max(0, Math.Min(5, textTone)) * 51);
            ink = new SolidColorBrush(Color.FromRgb(tone, tone, tone));
            Title = "任务备注";
            Width = 380;
            Height = 330;
            MinWidth = 340;
            MinHeight = 290;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(6),
                CornerRadius = new CornerRadius(0),
                GlassFrameThickness = new Thickness(0),
                UseAeroCaptionButtons = false
            });
            Content = BuildContent();
            SourceInitialized += delegate { ApplyBlur(); ScheduleRoundedWindowRegion(); };
            ContentRendered += delegate { ScheduleRoundedWindowRegion(); };
            Activated += delegate { ScheduleRoundedWindowRegion(); };
            Deactivated += delegate { ScheduleRoundedWindowRegion(); };
            SizeChanged += delegate { ScheduleRoundedWindowRegion(); };
        }

        private FrameworkElement BuildContent()
        {
            LinearGradientBrush edge = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            edge.GradientStops.Add(new GradientStop(Color.FromArgb(235, 255, 255, 255), 0));
            edge.GradientStops.Add(new GradientStop(Color.FromArgb(92, 255, 255, 255), .48));
            edge.GradientStops.Add(new GradientStop(Color.FromArgb(205, 5, 10, 12), 1));
            Border shell = new Border
            {
                CornerRadius = new CornerRadius(20),
                BorderThickness = new Thickness(1.3),
                BorderBrush = edge,
                Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255))
            };

            Grid root = new Grid { Margin = new Thickness(18, 10, 18, 16) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });

            Grid title = new Grid { Background = Brushes.Transparent };
            title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            title.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
            TextBlock heading = new TextBlock { Text = "任务备注", Foreground = ink, FontSize = 13, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(heading);
            Button close = FlatTextButton("×", 25);
            close.ToolTip = "取消并关闭";
            close.Click += delegate { DialogResult = false; };
            Grid.SetColumn(close, 1);
            title.Children.Add(close);
            root.Children.Add(title);

            StackPanel labels = new StackPanel { Margin = new Thickness(0, 4, 0, 6) };
            labels.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(task.Text) ? "未命名任务" : task.Text, Foreground = ink, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
            labels.Children.Add(new TextBlock { Text = "具体内容", Foreground = ink, Opacity = .70, FontSize = 11, Margin = new Thickness(0, 8, 0, 0) });
            Grid.SetRow(labels, 1);
            root.Children.Add(labels);

            noteBox = GlassTextBox(task.Note, true);
            Grid.SetRow(noteBox, 2);
            root.Children.Add(noteBox);

            StackPanel deadline = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            deadline.Children.Add(new TextBlock { Text = "时间要求", Foreground = ink, Opacity = .70, FontSize = 11, Margin = new Thickness(0, 0, 0, 5) });
            deadlineBox = GlassTextBox(task.Deadline, false);
            deadlineBox.Height = 35;
            deadline.Children.Add(deadlineBox);
            Grid.SetRow(deadline, 3);
            root.Children.Add(deadline);

            StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
            Button cancel = FlatTextButton("取消", 58);
            cancel.Opacity = .68;
            cancel.Click += delegate { DialogResult = false; };
            Button save = FlatTextButton("保存", 58);
            save.FontWeight = FontWeights.Bold;
            save.Click += delegate { SaveAndClose(); };
            actions.Children.Add(cancel);
            actions.Children.Add(save);
            Grid.SetRow(actions, 4);
            root.Children.Add(actions);

            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { e.Handled = true; DialogResult = false; }
                else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { e.Handled = true; SaveAndClose(); }
            };
            shell.Child = root;
            return shell;
        }

        private TextBox GlassTextBox(string value, bool multiline)
        {
            return new TextBox
            {
                Text = value ?? "",
                AcceptsReturn = multiline,
                TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
                VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
                Foreground = ink,
                CaretBrush = ink,
                Background = new SolidColorBrush(Color.FromArgb(38, 0, 0, 0)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(92, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 7, 9, 7),
                FontSize = 12,
                MaxLength = multiline ? 2000 : 200
            };
        }

        private Button FlatTextButton(string text, double width)
        {
            Button button = new Button { Content = text, Width = width, Height = 30, Foreground = ink, FontSize = 12, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
            ControlTemplate template = new ControlTemplate(typeof(Button));
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);
            template.VisualTree = border;
            button.Template = template;
            button.FocusVisualStyle = null;
            return button;
        }

        private void SaveAndClose()
        {
            task.Note = (noteBox.Text ?? "").Trim();
            task.Deadline = (deadlineBox.Text ?? "").Trim();
            DialogResult = true;
        }

        [StructLayout(LayoutKind.Sequential)] private struct AccentPolicy { public int AccentState; public int AccentFlags; public int GradientColor; public int AnimationId; }
        [StructLayout(LayoutKind.Sequential)] private struct CompositionData { public int Attribute; public IntPtr Data; public int SizeOfData; }
        [StructLayout(LayoutKind.Sequential)] private struct Margins { public int Left; public int Right; public int Top; public int Bottom; }
        [DllImport("user32.dll")] private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref CompositionData data);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
        [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

        private void ApplyRoundedWindowRegion()
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero || ActualWidth < 2 || ActualHeight < 2) return;
            uint dpi = GetDpiForWindow(handle);
            double scale = dpi == 0 ? 1 : dpi / 96.0;
            IntPtr region = CreateRoundRectRgn(0, 0, (int)Math.Ceiling(ActualWidth * scale) + 1, (int)Math.Ceiling(ActualHeight * scale) + 1, (int)Math.Round(40 * scale), (int)Math.Round(40 * scale));
            if (region == IntPtr.Zero) return;
            if (SetWindowRgn(handle, region, true) == 0) DeleteObject(region);
        }

        private void ScheduleRoundedWindowRegion()
        {
            ApplyRoundedWindowRegion();
            Dispatcher.BeginInvoke(new Action(ApplyRoundedWindowRegion), DispatcherPriority.ApplicationIdle);
        }

        private void ApplyBlur()
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            HwndSource source = HwndSource.FromHwnd(handle);
            if (source != null && source.CompositionTarget != null) source.CompositionTarget.BackgroundColor = Colors.Transparent;
            int nonClientRenderingDisabled = 1;
            DwmSetWindowAttribute(handle, 2, ref nonClientRenderingDisabled, sizeof(int));
            AccentPolicy policy = new AccentPolicy { AccentState = 3, AccentFlags = 2, GradientColor = unchecked((int)0x08FFFFFFu) };
            int size = Marshal.SizeOf(policy);
            IntPtr pointer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(policy, pointer, false);
                CompositionData data = new CompositionData { Attribute = 19, Data = pointer, SizeOfData = size };
                SetWindowCompositionAttribute(handle, ref data);
            }
            finally { Marshal.FreeHGlobal(pointer); }
            ApplyRoundedWindowRegion();
        }
    }

    public static class Program
    {
        public static bool Exiting { get; private set; }
        private static bool exiting;
        private static Forms.NotifyIcon tray;
        private static System.Threading.Mutex singleInstance;

        private static void WriteStartupLog(string message)
        {
            try
            {
                string directory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "daily-sticky");
                Directory.CreateDirectory(directory);
                File.AppendAllText(System.IO.Path.Combine(directory, "startup.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        private static Drawing.Bitmap CreateTrayIcon()
        {
            Drawing.Bitmap bmp = new Drawing.Bitmap(32, 32, Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Drawing.Graphics g = Drawing.Graphics.FromImage(bmp))
            using (Drawing.SolidBrush bg = new Drawing.SolidBrush(Drawing.Color.FromArgb(225, 30, 36, 42)))
            using (Drawing.Pen outline = new Drawing.Pen(Drawing.Color.FromArgb(235, 255, 255, 255), 1.5f))
            using (Drawing.SolidBrush dot = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 78, 180, 255)))
            {
                g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (Drawing.Drawing2D.GraphicsPath p = RoundedPath(new Drawing.Rectangle(3, 3, 26, 26), 7)) g.FillPath(bg, p);
                using (Drawing.Drawing2D.GraphicsPath p = RoundedPath(new Drawing.Rectangle(4, 4, 24, 24), 6)) g.DrawPath(outline, p);
                g.DrawLine(outline, 9, 11, 23, 11);
                g.DrawLine(outline, 10, 8, 10, 14);
                g.DrawLine(outline, 22, 8, 22, 14);
                g.FillEllipse(dot, 12, 16, 8, 8);
            }
            return bmp;
        }

        private static Drawing.Drawing2D.GraphicsPath RoundedPath(Drawing.Rectangle r, int radius)
        {
            int d = radius * 2;
            Drawing.Drawing2D.GraphicsPath p = new Drawing.Drawing2D.GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
        }

        [STAThread]
        public static void Main(string[] args)
        {
            WriteStartupLog("invoked; args=" + string.Join(" ", args ?? new string[0]) + "; exe=" + System.Reflection.Assembly.GetExecutingAssembly().Location);
            if (args != null && Array.Exists(args, delegate(string value) { return string.Equals(value, "--startup", StringComparison.OrdinalIgnoreCase); }))
            {
                WriteStartupLog("login launch detected; waiting 10 seconds");
                System.Threading.Thread.Sleep(10000);
            }
            bool createdNew;
            singleInstance = new System.Threading.Mutex(true, "DailyGlassNote.SingleInstance", out createdNew);
            if (!createdNew)
            {
                WriteStartupLog("another instance is already running; exit");
                return;
            }
            try
            {
            Application app = new Application { ShutdownMode = ShutdownMode.OnLastWindowClose };
            DailyStickyWindow main = new DailyStickyWindow();
            main.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                if (!exiting) { e.Cancel = true; main.Hide(); }
            };
            string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "daily-note-badge-04.ico");
            Drawing.Icon trayIcon = File.Exists(iconPath) ? new Drawing.Icon(iconPath) : Drawing.Icon.FromHandle(CreateTrayIcon().GetHicon());
            tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "每日便签", Visible = true };
            tray.DoubleClick += delegate { main.Show(); main.Activate(); };
            Forms.ContextMenuStrip menu = new Forms.ContextMenuStrip();
            menu.Items.Add("显示每日便签", null, delegate { main.Show(); main.Activate(); });
            menu.Items.Add("退出", null, delegate { exiting = true; Exiting = true; tray.Visible = false; tray.Dispose(); app.Shutdown(); });
            tray.ContextMenuStrip = menu;
            main.Show();
            string overrideDirectory = Environment.GetEnvironmentVariable("DAILY_STICKY_DATA_DIR");
            string directory = string.IsNullOrWhiteSpace(overrideDirectory) ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "daily-sticky") : overrideDirectory;
            if (Directory.Exists(directory))
                foreach (string path in Directory.GetFiles(directory, "daily-sticky-*.json"))
                {
                    string name = System.IO.Path.GetFileNameWithoutExtension(path);
                    if (!name.StartsWith("daily-sticky-", StringComparison.OrdinalIgnoreCase)) continue;
                    DailyStickyWindow restored = new DailyStickyWindow(name.Substring("daily-sticky-".Length), null);
                    if (restored.ShouldRestore) restored.Show();
                }
            // Historical secondary-window files are kept as data backups, but are
            // intentionally not auto-opened. This prevents closed notes from
            // reappearing in bulk on the next launch.
            app.Run();
            }
            catch (Exception ex)
            {
                WriteStartupLog("fatal: " + ex);
                try { Forms.MessageBox.Show("每日便签启动失败，错误已记录到：\n" + System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "daily-sticky", "startup.log"), "每日便签"); } catch { }
            }
            finally
            {
                if (singleInstance != null) { try { singleInstance.ReleaseMutex(); } catch { } singleInstance.Dispose(); }
            }
        }
    }
}
