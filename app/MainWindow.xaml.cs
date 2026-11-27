using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;
using Fluent = Wpf.Ui.Controls;

namespace TwinShift
{
    public partial class MainWindow : Window
    {
        readonly Preferences prefs;
        readonly AudioDeck deck;
        readonly Receiver receiver;
        readonly BadgeWindow badge;
        readonly Forms.NotifyIcon tray;
        readonly DispatcherTimer save = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(320)
        };
        readonly DispatcherTimer batch = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(180)
        };
        readonly Dictionary<string, DateTime> seen = new Dictionary<string, DateTime>();
        readonly Dictionary<string, Button> voiceButtons = new Dictionary<string, Button>();
        int activeManual, playbackSequence;
        bool ready, exiting, backupInvalid, changing;
        string pendingKind;
        string sceneFile;
        ApplicationTheme appliedTheme = ApplicationTheme.Light;
        DateTime lastVoice, lastAttention, manualUntil;
        string lastResultScope;
        DateTime lastResultAt;
        static readonly Dictionary<string, string> labels = new Dictionary<string, string>
        {
            {
                "permission",
                "等待权限审批"
            },
            {
                "question",
                "等待补充"
            },
            {
                "passed",
                "测试报告通过"
            },
            {
                "failed",
                "测试报告失败"
            },
            {
                "ended",
                "本轮聊天结束"
            },
            {
                "stopped",
                "本轮已中断"
            }
        };
        public MainWindow() : this(null) { }

        internal MainWindow(Process badgeTarget)
        {
            InitializeComponent();
            prefs = Preferences.Load(out string warning);
            backupInvalid = warning.Length > 0;
            deck = new AudioDeck(prefs);
            deck.Notice += AudioStatus;
            deck.Started += key => AudioStatus("正在播放 · " + ClipTitle(key));
            badge = new BadgeWindow(prefs, badgeTarget, () => Manual("tutel"), () =>
            {
                Reveal();
                Page(2);
            });
            receiver = new Receiver();
            receiver.Received += (k, id, s) => Dispatcher.BeginInvoke(new Action(() => Event(k, id, s)));
            receiver.Start();
            tray = new Forms.NotifyIcon
            {
                Icon = new System.Drawing.Icon(Files.Asset("twinshift.ico")),
                Text = "Vedal AI",
                Visible = true
            };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("打开Vedal AI", null, (s, e) => Reveal());
            menu.Items.Add("退出", null, (s, e) => Exit());
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += (s, e) => Reveal();
            Icon = Files.Image("twinshift.ico");
            TutelImage.Source = Files.Image("tutel.png");
            string[] pages =
            {
                "外观",
                "原声",
                "任务",
                "接入"
            };
            Fluent.SymbolRegular[] icons =
            {
                Fluent.SymbolRegular.Desktop24,
                Fluent.SymbolRegular.Headphones24,
                Fluent.SymbolRegular.Pulse24,
                Fluent.SymbolRegular.PlugConnected24
            };
            for (int i = 0; i < pages.Length; i++)
            {
                int ix = i;
                var panel = new StackPanel
                {
                    Orientation = Orientation.Horizontal
                };
                panel.Children.Add(new Fluent.SymbolIcon { Symbol = icons[i], FontSize = 17, VerticalAlignment = VerticalAlignment.Center });
                panel.Children.Add(new TextBlock { Text = pages[i], Margin = new Thickness(10, 0, 0, 0) });
                var b = Btn(panel, () => Page(ix));
                b.Style = (Style)FindResource("NavButton");
                AutomationProperties.SetName(b, pages[i]);
                Nav.Children.Add(b);
            }

            foreach (var c in deck.Clips.Values)
                CreateVoiceRow(c);
            save.Tick += (s, e) =>
            {
                save.Stop();
                Persist();
            };
            batch.Tick += (s, e) => FlushEvent();
            SystemEvents.UserPreferenceChanged += SystemChanged;
            Closing += (s, e) =>
            {
                if (!exiting)
                {
                    e.Cancel = true;
                    Hide();
                }
            };
            Closed += (s, e) =>
            {
                SystemEvents.UserPreferenceChanged -= SystemChanged;
                batch.Stop();
                if (save.IsEnabled)
                {
                    save.Stop();
                    Persist();
                }

                badge.Finish();
                receiver.Dispose();
                deck.Dispose();
                tray.Visible = false;
                tray.Dispose();
                menu.Dispose();
                Application.Current.Shutdown();
            };
            Sync();
            ready = true;
            Apply();
            Page(0);
            if (backupInvalid)
                Say(warning);
        }

        void SystemChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!exiting)
                    Apply();
            }));
        }

        void Exit()
        {
            if (changing)
            {
                Say("请等待插件操作完成。");
                Reveal();
                return;
            }

            exiting = true;
            Close();
        }

        internal void Reveal()
        {
            Show();
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
        }

        void Say(string msg)
        {
            Feedback.Text = msg;
        }

        static Glyph Mark(string name, int size) => new Glyph
        {
            Kind = name,
            Width = size,
            Height = size,
            VerticalAlignment = VerticalAlignment.Center
        };
        static Button Btn(object label, Action action)
        {
            var b = new Button
            {
                Content = label
            };
            b.Click += (s, e) => action();
            return b;
        }

        static void OpenFile(string path)
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }

        void Guard(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Say("操作未完成。" + e.Message);
            }
        }

        void Page(int index)
        {
            var pages = new[]
            {
                Appearance,
                Voices,
                Activity,
                Setup
            };
            for (int i = 0; i < pages.Length; i++)
            {
                pages[i].Visibility = index == i ? Visibility.Visible : Visibility.Collapsed;
                AutomationProperties.SetItemStatus((Button)Nav.Children[i], index == i ? "已选中" : "");
            }

            BodyScroll.ScrollToTop();
        }

        void Sync()
        {
            BadgeToggle.IsChecked = prefs.Badge;
            MotionToggle.IsChecked = prefs.QuietMotion;
            OnlineToggle.IsChecked = prefs.Online;
            AutoToggle.IsChecked = prefs.AutoVoice;
            AttentionToggle.IsChecked = prefs.Attention;
            MuteToggle.IsChecked = prefs.Muted;
            VolumeSlider.Value = prefs.Volume;
            VolumeText.Text = prefs.Volume + "%";
        }

        void Persist()
        {
            Guard(() =>
            {
                if (backupInvalid && File.Exists(Preferences.FileName))
                {
                    File.Copy(Preferences.FileName, Preferences.FileName + "." + Guid.NewGuid().ToString("N") + ".bak");
                    backupInvalid = false;
                }

                prefs.Save();
            });
        }

        void SaveSoon()
        {
            save.Stop();
            save.Start();
        }

        void Apply()
        {
            bool night = prefs.Night;
            bool hc = SystemParameters.HighContrast;
            var theme = hc ? ApplicationTheme.HighContrast : night ? ApplicationTheme.Dark : ApplicationTheme.Light;
            if (appliedTheme != theme)
            {
                Application.Current.Resources.MergedDictionaries.OfType<ThemesDictionary>().Single().Theme = theme;
                appliedTheme = theme;
            }
            string[] keys =
            {
                "Paper",
                "Card",
                "Soft",
                "Ink",
                "Muted",
                "Edge",
                "Accent",
                "Good"
            };
            string[] colors = night ? new[]
            {
                "#0A0A0A",
                "#171717",
                "#252525",
                "#EBEBEB",
                "#ADADAD",
                "#404040",
                "#C9C9C9",
                "#90CEA0"
            }

            : new[]
            {
                "#F9F4EC",
                "#FFFCF7",
                "#E6ECE1",
                "#2B3932",
                "#565F56",
                "#D9CFC4",
                "#5D7559",
                "#246148"
            };
            for (int i = 0; i < keys.Length; i++)
            {
                var brush = hc ? (i <= 2 ? SystemColors.WindowBrush : i == 6 ? SystemColors.HighlightBrush : SystemColors.WindowTextBrush) : new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
                Application.Current.Resources[keys[i]] = brush;
            }

            Application.Current.Resources["OnAccent"] = hc ? SystemColors.HighlightTextBrush :
                (night ? Brushes.Black : Brushes.White);
            ApplicationAccentColorManager.Apply(((SolidColorBrush)Application.Current.Resources["Accent"]).Color, theme, false, false);
            foreach (var button in new[] { SystemButton, NeoButton, EvoButton })
            {
                bool selected = (string)button.Tag == prefs.Mode;
                button.SetResourceReference(BackgroundProperty, selected ? "Card" : "Paper");
                button.SetResourceReference(BorderBrushProperty, selected ? "Edge" : "Paper");
                button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
                System.Windows.Automation.AutomationProperties.SetItemStatus(button, selected ? "已选中" : "");
            }
            foreach (var button in new[] { CopyNeo, CopyEvo })
            {
                bool active = button == (night ? CopyEvo : CopyNeo);
                button.SetResourceReference(BackgroundProperty, active ? "Accent" : "Card");
                button.SetResourceReference(ForegroundProperty, active ? "OnAccent" : "Ink");
            }
            string image = night ? "evo.jpg" : "neo.jpg";
            if (sceneFile != image)
            {
                Scene.ImageSource = Files.Image(image);
                sceneFile = image;
            }
            HeroKicker.Text = night ? "Evo  ·  地下室" : "Neo  ·  复古电台";
            HeroTitle.Text = night ? "月之暗面" : "月之亮面";
            HeroDetails.Children.Clear();
            foreach (string name in night ? new[]
            {
                "broken",
                "pipes",
                "headphones"
            }

            : new[]
            {
                "heart",
                "crt",
                "lamp"
            }

            )
            {
                var g = Mark(name, 18);
                g.Stroke = Brushes.White;
                g.Margin = new Thickness(0, 0, 9, 0);
                HeroDetails.Children.Add(g);
            }

            ShiftStatus.Text = night ? "Evo · 月之暗面" : "Neo · 月之亮面";
            badge.Apply();
        }

        void ModeClick(object s, RoutedEventArgs e)
        {
            prefs.Mode = (string)((Button)s).Tag;
            pendingKind = null;
            CancelPlayback("切换外观后已停止播放。");
            Apply();
            SaveSoon();
        }

        void PreferenceClick(object s, RoutedEventArgs e)
        {
            if (!ready)
                return;
            prefs.Badge = BadgeToggle.IsChecked == true;
            prefs.QuietMotion = MotionToggle.IsChecked == true;
            prefs.AutoVoice = AutoToggle.IsChecked == true;
            prefs.Attention = AttentionToggle.IsChecked == true;
            prefs.Muted = MuteToggle.IsChecked == true;
            if (prefs.Online && OnlineToggle.IsChecked != true)
            {
                CancelPlayback("已关闭后台取音并清除临时片段。");
                deck.ClearOnline();
            }
            else if (activeManual == 0 && (s == MuteToggle || s == AutoToggle || s == AttentionToggle))
                CancelPlayback("自动播放设置已更新。");
            prefs.Online = OnlineToggle.IsChecked == true;
            pendingKind = null;
            RefreshVoiceButtons();
            badge.Apply();
            SaveSoon();
        }

        void VolumeChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!ready)
                return;
            prefs.Volume = (int)Math.Round(VolumeSlider.Value);
            VolumeText.Text = prefs.Volume + "%";
            deck.SetLevel();
            if (prefs.Volume == 0)
                CancelPlayback("音量为零。");
            SaveSoon();
        }

        void DefaultPrefs(object s, RoutedEventArgs e)
        {
            prefs.Mode = "system";
            prefs.Badge = prefs.Online = prefs.AutoVoice = prefs.Muted = prefs.Attention = prefs.QuietMotion = false;
            prefs.Volume = 60;
            deck.ClearOnline();
            RefreshVoiceButtons();
            Sync();
            Apply();
            SaveSoon();
            Say("已恢复默认偏好，原主题备份和自选 WAV 已保留。");
        }

        void OpenGuide(object s, RoutedEventArgs e) => Guard(() => OpenFile(Path.Combine(Files.Base, "使用手册.html")));
        void OpenCredits(object s, RoutedEventArgs e) => Guard(() => OpenFile(new Uri(Path.Combine(Files.Base, "使用手册.html")).AbsoluteUri + "#credits"));
        void OpenWallpaperRelease(object s, RoutedEventArgs e) => Guard(() => OpenFile("https://github.com/Fei-Away/Codex-Dream-Skin/releases/tag/v1.5.21"));
        void ShowScenes(object s, RoutedEventArgs e) => Guard(() => OpenFile(Path.Combine(Files.Base, "scenes")));
        void EnableWallpaperHeader(object s, RoutedEventArgs e) => SetWallpaperHeader(true);
        void RestoreWallpaperHeader(object s, RoutedEventArgs e) => SetWallpaperHeader(false);
        void SetWallpaperHeader(bool enable) => Guard(() =>
        {
            string engine = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexDreamSkin", "engine");
            bool changed = WallpaperHeader.Change(engine, enable);
            Say(changed ? "已更新标题栏资源。请在壁纸宿主中重新应用主题。" : "标题栏已是所选状态。");
        });
        void CopyTheme(object s, RoutedEventArgs e) => Guard(() =>
        {
            string key = (string)((Button)s).Tag;
            Clipboard.SetText(File.ReadAllText(Path.Combine(Files.Base, "themes", key + ".txt")).Trim());
            Say("主题已复制。请在 Codex 外观设置中导入对应槽位。");
        });
        void RestoreTheme(object s, RoutedEventArgs e) => Guard(() =>
        {
            string file = Path.Combine(SignalBus.Root, "backup-" + ((Button)s).Tag + ".txt");
            if (!File.Exists(file))
            {
                Say("此槽位还没有备份。");
                return;
            }

            Clipboard.SetText(File.ReadAllText(file));
            Say("原主题已复制，可在 Codex 对应槽位导回。");
        });
        void BackupTheme(object s, RoutedEventArgs e) => Guard(() =>
        {
            string value = Clipboard.GetText().Trim();
            const string prefix = "codex-theme-v1:";
            if (!value.StartsWith(prefix, StringComparison.Ordinal) || value.Length > 16384)
            {
                Say("请先在 Codex 外观设置中复制原主题。");
                return;
            }

            var json = value.Substring(prefix.Length);
            if (!json.StartsWith("{"))
                json = Uri.UnescapeDataString(json);
            var obj = new System.Web.Script.Serialization.JavaScriptSerializer
            {
                RecursionLimit = 16
            }.DeserializeObject(json) as Dictionary<string, object>;
            if (obj == null || !obj.ContainsKey("theme") || !obj.TryGetValue("variant", out var v) || (!(v as string == "light") && !(v as string == "dark")))
            {
                Say("无法识别主题槽位。");
                return;
            }

            string file = Path.Combine(SignalBus.Root, "backup-" + v + ".txt");
            if (File.Exists(file) && MessageBox.Show(this, "要替换此槽位已有的备份吗？", "备份原主题", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                return;
            Files.AtomicText(file, value);
            Say("已备份 " + v + " 槽位。");
        });
        void CreateVoiceRow(Cue cue)
        {
            var row = new DockPanel
            {
                LastChildFill = true,
                Margin = new Thickness(0, 3, 0, 3)
            };
            var menu = new ContextMenu();
            var src = new MenuItem
            {
                Header = "查看剪辑出处"
            };
            src.Click += (s, e) => Guard(() =>
            {
                if (Uri.TryCreate(cue.Source, UriKind.Absolute, out var u) && u.Scheme == "https")
                    OpenFile(u.AbsoluteUri);
            });
            menu.Items.Add(src);
            var import = new MenuItem
            {
                Header = "使用本地 WAV…"
            };
            import.Click += (s, e) => Guard(() =>
            {
                var dlg = new OpenFileDialog
                {
                    Filter = "PCM WAV|*.wav"
                };
                if (dlg.ShowDialog(this) == true)
                {
                    deck.Import(cue, dlg.FileName);
                    RefreshVoiceButtons();
                    AudioStatus("已导入 · " + cue.Line);
                }
            });
            menu.Items.Add(import);
            var remove = new MenuItem
            {
                Header = "移除此句的本地副本"
            };
            remove.Click += (s, e) => Guard(() =>
            {
                if (deck.HasLocal(cue) && MessageBox.Show(this, "移除此句导入的副本？原文件保留。", "本地语音", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    deck.RemoveLocal(cue);
                    RefreshVoiceButtons();
                    AudioStatus("本地副本已移除。");
                }
            });
            menu.Items.Add(remove);
            var more = new Button
            {
                Content = "⋯",
                Style = (Style)FindResource("QuietButton"),
                ContextMenu = menu,
                ToolTip = "本地音频与出处",
                MinWidth = 32
            };
            AutomationProperties.SetName(more, cue.Line + " 更多选项");
            menu.Opened += (s, e) => remove.IsEnabled = deck.HasLocal(cue);
            more.Click += (s, e) =>
            {
                menu.PlacementTarget = more;
                menu.IsOpen = true;
            };
            DockPanel.SetDock(more, Dock.Right);
            row.Children.Add(more);
            var play = new Button { MinWidth = 80, VerticalAlignment = VerticalAlignment.Center };
            play.Click += (s, e) => Manual(cue.Id, Equals(play.Tag, true));
            voiceButtons.Add(cue.Id, play);
            UpdateVoiceButton(cue, play);
            DockPanel.SetDock(play, Dock.Right);
            row.Children.Add(play);
            var text = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
            text.Children.Add(new TextBlock { Text = cue.Speaker + "  ·  " + cue.Slot, Style = (Style)FindResource("Hint") });
            var line = new TextBlock
            {
                Text = cue.Line,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold
            };
            text.Children.Add(line);
            text.Children.Add(new TextBlock { Text = cue.Translation, Style = (Style)FindResource("Hint") });
            row.Children.Add(text);
            var card = new Border
            {
                Child = row,
                Padding = new Thickness(1, 7, 0, 7),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            card.SetResourceReference(Border.BorderBrushProperty, "Edge");
            VoiceRows.Children.Add(card);
        }

        void UpdateVoiceButton(Cue cue, Button play)
        {
            bool local = deck.HasLocal(cue), cached = deck.HasSessionClip(cue);
            play.Tag = !local && !cached;
            play.Content = local ? "本地试听" : cached ? "重播" : "联网试听";
            play.ToolTip = local ? "播放已导入的 WAV" : cached ? "本次会话离线重播" : "只从公开片源获取这一句";
            AutomationProperties.SetName(play, play.Content + " " + cue.Speaker + " " + cue.Line);
        }

        void RefreshVoiceButtons()
        {
            foreach (var pair in voiceButtons)
                UpdateVoiceButton(deck.Clips[pair.Key], pair.Value);
        }

        string ClipTitle(string key) => key == "attention" ? "审批提示音" : deck.Clips[key].Speaker + " · " + deck.Clips[key].Line;

        void AudioStatus(string message)
        {
            VoiceFeedback.Text = message;
            Say(message);
        }

        async Task PlayCue(string key, bool once = false)
        {
            int sequence = ++playbackSequence;
            StopAudio.IsEnabled = true;
            try
            {
                if (await deck.Play(key, once))
                    AudioStatus("已播完 · " + ClipTitle(key));
            }
            finally
            {
                if (sequence == playbackSequence)
                {
                    StopAudio.IsEnabled = false;
                    RefreshVoiceButtons();
                }
            }
        }

        async void Manual(string key, bool once = false)
        {
            var cue = deck.Clips[key];
            if (!once && !prefs.Online && !deck.HasLocal(cue) && !deck.HasSessionClip(cue))
            {
                Reveal();
                Page(1);
                voiceButtons[key].Focus();
                voiceButtons[key].BringIntoView();
                AudioStatus("试听 " + cue.Speaker + " 的这句台词，请点「联网试听」。");
                return;
            }
            activeManual++;
            manualUntil = DateTime.UtcNow.AddSeconds(4);
            try { await PlayCue(key, once); }
            finally { activeManual--; }
        }

        void CancelPlayback(string message)
        {
            deck.Stop();
            if (StopAudio.IsEnabled)
                AudioStatus(message);
        }

        void StopClick(object s, RoutedEventArgs e)
        {
            CancelPlayback("已停止播放。");
            pendingKind = null;
        }

        void TurtleClick(object s, RoutedEventArgs e) => Manual("tutel");
        void Event(string kind, string id, string scope)
        {
            if (exiting)
                return;
            if (kind == "show")
            {
                Reveal();
                return;
            }

            if (!labels.ContainsKey(kind))
                return;
            DateTime now = DateTime.UtcNow;
            foreach (var old in seen.Where(p => p.Value < now.AddMinutes(-2)).Select(p => p.Key).ToArray())
                seen.Remove(old);
            if (seen.ContainsKey(id))
                return;
            if (seen.Count >= 256)
                seen.Remove(seen.OrderBy(p => p.Value).First().Key);
            seen[id] = now;
            if (History.Children.Count >= 12)
                History.Children.RemoveAt(History.Children.Count - 1);
            string info = DateTime.Now.ToString("HH:mm:ss") + "  ·  #" + scope.Substring(0, 6);
            var b = Btn(info + "    " + labels[kind], () => SelectResult(kind, info));
            b.HorizontalContentAlignment = HorizontalAlignment.Left;
            History.Children.Insert(0, b);
            bool keepResult = kind == "ended" && scope == lastResultScope && now - lastResultAt < TimeSpan.FromSeconds(5);
            if (!keepResult)
            {
                SelectResult(kind, info);
                lastResultScope = kind == "passed" || kind == "failed" ? scope : null;
                lastResultAt = now;
                badge.Pulse(kind == "passed" ? (prefs.Night ? "pipes" : "heart") : kind == "failed" ? "broken" : kind == "permission" ? "shield" : "radio");
            }

            int rank = Priority(kind);
            if (rank > 0 && rank >= Priority(pendingKind))
            {
                pendingKind = kind;
                if (!batch.IsEnabled)
                    batch.Start();
            }
        }

        int Priority(string k) => k == "permission" || k == "question" ? 3 : k == "failed" ? 2 : k == "passed" ? 1 : 0;
        void SelectResult(string kind, string info)
        {
            LastTitle.Text = labels[kind];
            LastDetail.Text = info;
            ResultActions.Children.Clear();
            if (kind == "passed")
            {
                string key = prefs.Night ? "win" : "yay";
                ResultActions.Children.Add(Btn("试听收工台词", () => Manual(key)));
                if (prefs.Night)
                    ResultActions.Children.Add(Btn("管道庆功", () => Manual("pipes")));
                ResultActions.Children.Add(Btn("点一下龟龟", () => Manual("vedal")));
            }

            if (kind == "failed" && prefs.Night)
                ResultActions.Children.Add(Btn("听听 Evil", () => Manual("shucks")));
        }

        async void FlushEvent()
        {
            batch.Stop();
            string k = pendingKind;
            pendingKind = null;
            DateTime now = DateTime.UtcNow;
            if (k == null || prefs.Muted || prefs.Volume == 0 || activeManual > 0 || now < manualUntil)
                return;
            bool attention = k == "permission" || k == "question";
            if (attention)
            {
                if (!prefs.Attention || now - lastAttention < TimeSpan.FromSeconds(5))
                    return;
                lastAttention = now;
                await PlayCue("attention");
            }
            else if (prefs.AutoVoice && (k == "passed" || prefs.Night) && now - lastVoice >= TimeSpan.FromSeconds(5))
            {
                lastVoice = now;
                await PlayCue(k == "failed" ? "shucks" : prefs.Night ? "win" : "yay");
            }
        }

        async void PluginAction(bool enable)
        {
            if (changing)
                return;
            changing = true;
            EnablePlugin.IsEnabled = DisablePlugin.IsEnabled = false;
            Say("Codex 正在处理本地插件…");
            try
            {
                Say(await PluginManager.Change(enable));
            }
            catch (Exception e)
            {
                Say(e.Message);
            }
            finally
            {
                changing = false;
                EnablePlugin.IsEnabled = DisablePlugin.IsEnabled = true;
            }
        }

        void EnableClick(object s, RoutedEventArgs e) => PluginAction(true);
        void DisableClick(object s, RoutedEventArgs e) => PluginAction(false);
    }
}
