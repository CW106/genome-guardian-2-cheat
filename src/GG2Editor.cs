// Genome Guardian 2 存檔修改器 (C# 5 / .NET Framework 4.8, build with build.bat)
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: AssemblyTitle("GG2 存檔修改器")]
[assembly: AssemblyVersion("1.0.0.0")]

namespace GG2Editor
{
    // ---------- 存檔模型 ----------
    class Entry { public string Key; public object Value; }

    class SaveData
    {
        static readonly byte[] XorKey = Encoding.ASCII.GetBytes("alphakenneth");
        public List<string> Order = new List<string>();
        public Dictionary<string, List<Entry>> Sections = new Dictionary<string, List<Entry>>();

        public static byte[] Xor(byte[] d)
        {
            var o = new byte[d.Length];
            for (int i = 0; i < d.Length; i++) o[i] = (byte)(d[i] ^ XorKey[i % XorKey.Length]);
            return o;
        }

        public static SaveData Load(string path)
        {
            string json = Encoding.UTF8.GetString(Xor(File.ReadAllBytes(path)));
            var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 };
            var root = js.DeserializeObject(json) as Dictionary<string, object>;
            if (root == null || !root.ContainsKey("IntData")) throw new InvalidDataException("不是 Genome Guardian 2 的存檔（解碼後格式不符）");
            var s = new SaveData();
            foreach (var kv in root)
            {
                s.Order.Add(kv.Key);
                var list = new List<Entry>();
                var arr = kv.Value as object[];
                if (arr != null)
                    foreach (var o in arr)
                    {
                        var d = (Dictionary<string, object>)o;
                        list.Add(new Entry { Key = (string)d["Key"], Value = d["Value"] });
                    }
                s.Sections[kv.Key] = list;
            }
            return s;
        }

        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append('{');
            for (int i = 0; i < Order.Count; i++)
            {
                if (i > 0) sb.Append(',');
                Str(sb, Order[i]); sb.Append(":[");
                var list = Sections[Order[i]];
                for (int j = 0; j < list.Count; j++)
                {
                    if (j > 0) sb.Append(',');
                    sb.Append("{\"Key\":"); Str(sb, list[j].Key); sb.Append(",\"Value\":");
                    Val(sb, list[j].Value); sb.Append('}');
                }
                sb.Append(']');
            }
            sb.Append('}');
            return sb.ToString();
        }

        static void Val(StringBuilder sb, object v)
        {
            if (v is string) Str(sb, (string)v);
            else if (v is bool) sb.Append((bool)v ? "true" : "false");
            else if (v is double) sb.Append(((double)v).ToString("R", CultureInfo.InvariantCulture));
            else if (v is float) sb.Append(((double)(float)v).ToString("R", CultureInfo.InvariantCulture));
            else if (v is decimal) sb.Append(((decimal)v).ToString(CultureInfo.InvariantCulture));
            else if (v == null) sb.Append("null");
            else sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture));
        }

        static void Str(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else if (c < 0x20 || c > 0x7e) sb.AppendFormat("\\u{0:x4}", (int)c);
                else sb.Append(c);
            }
            sb.Append('"');
        }

        public Entry Find(string section, string key)
        {
            List<Entry> l;
            if (!Sections.TryGetValue(section, out l)) return null;
            foreach (var e in l) if (e.Key == key) return e;
            return null;
        }
        public string GetStr(string key) { var e = Find("StringData", key); return e == null ? null : e.Value as string; }
        public void SetStr(string key, string v) { var e = Find("StringData", key); if (e != null) e.Value = v; }
        public long GetInt(string key) { var e = Find("IntData", key); return e == null ? 0 : Convert.ToInt64(e.Value, CultureInfo.InvariantCulture); }
        public void SetInt(string key, long v) { var e = Find("IntData", key); if (e != null) e.Value = (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, v)); }
        public bool HasBool(string key) { return Find("BoolData", key) != null; }
        public bool GetBool(string key) { var e = Find("BoolData", key); return e != null && e.Value is bool && (bool)e.Value; }
        public void SetBool(string key, bool v) { var e = Find("BoolData", key); if (e != null) e.Value = v; }
    }

    // "id,v|id,v" / "a,b,v|..." 形式的清單
    class PairList
    {
        public List<string[]> Items = new List<string[]>();
        public static PairList Parse(string s)
        {
            var p = new PairList();
            if (!string.IsNullOrEmpty(s))
                foreach (var part in s.Split('|')) p.Items.Add(part.Split(','));
            return p;
        }
        public override string ToString() { return string.Join("|", Items.Select(a => string.Join(",", a))); }
        public string[] FindPrefix(params string[] prefix)
        {
            foreach (var a in Items)
            {
                if (a.Length < prefix.Length) continue;
                bool ok = true;
                for (int i = 0; i < prefix.Length; i++) if (a[i] != prefix[i]) { ok = false; break; }
                if (ok) return a;
            }
            return null;
        }
    }

    // ---------- 名稱表（從遊戲本體解出，內嵌資源 names.json） ----------
    static class Names
    {
        static Dictionary<string, object> root;
        public static void Init()
        {
            try
            {
                using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("names.json"))
                using (var r = new StreamReader(st, Encoding.UTF8))
                    root = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(r.ReadToEnd()) as Dictionary<string, object>;
            }
            catch { root = new Dictionary<string, object>(); }
        }
        public static Dictionary<string, object> Item(string cat, string id)
        {
            object c, i;
            if (root.TryGetValue(cat, out c) && c is Dictionary<string, object> && ((Dictionary<string, object>)c).TryGetValue(id, out i))
                return i as Dictionary<string, object>;
            return null;
        }
        static string S(Dictionary<string, object> d, string k)
        {
            object v; return d != null && d.TryGetValue(k, out v) ? v as string : null;
        }
        static string Sub(Dictionary<string, object> d, string k, string lang)
        {
            object v; if (d == null || !d.TryGetValue(k, out v)) return null;
            return S(v as Dictionary<string, object>, lang);
        }
        public static string Label(string cat, string id)
        {
            var it = Item(cat, id);
            string zh = S(it, "zh"), en = S(it, "en");
            if (zh == null && en == null) return "#" + id;
            if (zh == null || zh == en) return "#" + id + "  " + en;
            return "#" + id + "  " + zh + "  /  " + en;
        }
        public static string Detail(string cat, string id)
        {
            var it = Item(cat, id);
            if (it == null) return "";
            var sb = new StringBuilder();
            string d = Sub(it, "description", "zh"); if (d != null) sb.AppendLine("說明：" + d);
            string c = Sub(it, "condition", "zh"); if (c != null) sb.AppendLine("原本解鎖條件：" + c);
            return sb.ToString().TrimEnd();
        }
        public static int LevelMax(string tree, string id)
        {
            var it = Item("research", tree + "," + id);
            object v; return it != null && it.TryGetValue("levelMax", out v) ? Convert.ToInt32(v) : -1;
        }
    }

    // ---------- 主視窗 ----------
    partial class MainForm : Form
    {
        static readonly string SaveDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"AppData\LocalLow\Alpherior\Genome Guardian 2");
        static readonly string[] SaveFiles = { "genome-guardian-2.txt", "genome-guardian-2-backup.txt", "genome-guardian-2-rescue.txt" };
        static readonly string BackupRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "存檔備份");

        static readonly string[][] UnlockCats = {
            new[]{"guardian","守護者"}, new[]{"hull","船體"}, new[]{"platform","平台"}, new[]{"barrel","槍管"},
            new[]{"bracket","支架"}, new[]{"core","核心"}, new[]{"motherboard","主機板"}, new[]{"gadget","裝置"},
            new[]{"mod","模組"}, new[]{"paint","塗裝"}, new[]{"pattern","圖案"}, new[]{"cursor","游標"},
            new[]{"crosshair","準星"}, new[]{"cone","瞄準錐"}, new[]{"mode","模式 × 深度"},
        };
        static readonly string[][] FlagKeys = {
            new[]{"dailyDiveUnlocked","每日潛入 (Daily Dive)"}, new[]{"depthXUnlocked","深度 X (DEPTH X)"},
            new[]{"uracilUnlocked","尿嘧啶 (Uracil)"}, new[]{"zoneUnlocked","區域 (Zone)"},
        };

        SaveData save;
        bool dirty, loading;
        readonly Label lblPath = new Label(), lblStatus = new Label();
        readonly TabControl tabs = new TabControl();
        readonly NumericUpDown numResearch = new NumericUpDown(), numHammers = new NumericUpDown();
        readonly ListBox lstCats = new ListBox();
        readonly CheckedListBox lstItems = new CheckedListBox();
        readonly TextBox txtDetail = new TextBox();
        readonly FlowLayoutPanel pnlFlags = new FlowLayoutPanel();
        readonly DataGridView gridResearch = new DataGridView(), gridAll = new DataGridView();
        readonly TextBox txtFilter = new TextBox();
        readonly Timer gameTimer = new Timer();

        public MainForm()
        {
            Text = "Genome Guardian 2 存檔修改器";
            Font = new Font("Microsoft JhengHei UI", 10f);
            float k;
            using (var g = CreateGraphics()) k = g.DpiX / 96f;
            Size = new Size((int)(1000 * k), (int)(720 * k));
            MinimumSize = new Size((int)(820 * k), (int)(560 * k));
            StartPosition = FormStartPosition.CenterScreen;

            // 上方工具列
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6), WrapContents = true };
            top.Controls.Add(Btn("重新讀取", (s, e) => { if (ConfirmDiscard()) LoadSave(); }));
            top.Controls.Add(Btn("儲存到遊戲", (s, e) => DoSave()));
            top.Controls.Add(Btn("一鍵全解鎖", (s, e) => UnlockEverything()));
            top.Controls.Add(Btn("開啟備份資料夾", (s, e) => { Directory.CreateDirectory(BackupRoot); Process.Start("explorer.exe", "\"" + BackupRoot + "\""); }));
            top.Controls.Add(Btn("還原備份…", (s, e) => RestoreBackup()));
            lblStatus.AutoSize = true; lblStatus.MinimumSize = new Size(360, 0); lblStatus.Margin = new Padding(12, 9, 0, 0);
            top.Controls.Add(lblStatus);
            lblPath.Dock = DockStyle.Bottom; lblPath.Height = 24; lblPath.ForeColor = Color.DimGray; lblPath.Padding = new Padding(6, 3, 0, 0);

            tabs.Dock = DockStyle.Fill;
            tabs.TabPages.Add(BuildResourcesTab());
            tabs.TabPages.Add(BuildUnlocksTab());
            tabs.TabPages.Add(BuildResearchTab());
            tabs.TabPages.Add(BuildAllTab());
            tabs.TabPages.Add(BuildLiveTab());
            tabs.SelectedIndexChanged += (s, e) => RefreshTab();

            Controls.Add(tabs); Controls.Add(top); Controls.Add(lblPath);

            gameTimer.Interval = 1500;
            gameTimer.Tick += (s, e) => { AutoReload(); UpdateStatus(); };
            gameTimer.Start();
            FormClosing += (s, e) => { if (!ConfirmDiscard()) e.Cancel = true; };
            FormClosed += (s, e) => LiveShutdown();
            liveTimer.Interval = 100;
            liveTimer.Tick += (s, e) => LiveTick();
            liveTimer.Start();
            Shown += (s, e) => LoadSave();
        }

        static Button Btn(string text, EventHandler h)
        {
            var b = new Button { Text = text, AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
            b.Click += h; return b;
        }

        // ---- 分頁：資源 ----
        TabPage BuildResourcesTab()
        {
            var p = new TabPage("資源") { AutoScroll = true };
            var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Padding = new Padding(16) };
            SetupNum(numResearch, "research"); SetupNum(numHammers, "hammers");
            AddRow(t, "研究點數 (Research)", numResearch, "研究選單裡花的貨幣");
            AddRow(t, "酵素 (Enzymes)", numHammers, "強制解鎖物品用（存檔裡叫 hammers）");

            // 目前這一局：runSave 以 ~ 分隔，索引由 Run.SaveRun / LoadRun 反組譯確認
            grpRun.Text = "目前這一局";
            grpRun.Dock = DockStyle.Top; grpRun.AutoSize = true; grpRun.Padding = new Padding(10);
            var rt = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3 };
            foreach (var f in RunFields)
            {
                var n = new NumericUpDown { Minimum = 0, Maximum = 999999, Width = 160, ThousandsSeparator = true, Tag = f[0] };
                n.ValueChanged += OnRunEdited;
                runNums.Add(n);
                AddRow(rt, f[1], n, f[2]);
            }
            lblRunNone.AutoSize = true; lblRunNone.ForeColor = Color.DimGray; lblRunNone.Dock = DockStyle.Top;
            lblRunNone.Text = "現在沒有進行中的局。在遊戲裡開一局、到商店畫面時存檔離開，這裡就會自動出現。";
            grpRun.Controls.Add(rt); grpRun.Controls.Add(lblRunNone);
            var note = new Label
            {
                Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(16), ForeColor = Color.DimGray,
                Text = "使用方式：先完全關閉遊戲 → 在這裡調整 → 按「儲存到遊戲」→ 再開遊戲。\r\n" +
                       "每次儲存前都會自動把原本的存檔備份到「存檔備份」資料夾，改壞了可以用「還原備份…」救回來。\r\n" +
                       "「進階」分頁可以直接改存檔裡的任何數值；「即時（遊戲中）」分頁可以在遊戲開著時開無敵。"
            };
            // Dock=Top 時最後加入的排在最上面
            p.Controls.Add(note);
            p.Controls.Add(grpRun);
            p.Controls.Add(t);
            return p;
        }
        // {runSave 索引, 名稱, 說明}
        static readonly string[][] RunFields = {
            new[]{"6","生物質 (Biomass)","局內商店的貨幣"},
            new[]{"37","目前血量 (Health)","超過上限的部分可能被遊戲砍掉，建議同時加血量上限"},
            new[]{"14","血量上限加成","加在原本的血量上限上"},
            new[]{"35","目前護甲 (Armour)",""},
            new[]{"12","護甲上限加成",""},
            new[]{"36","目前護盾 (Shield)",""},
            new[]{"13","護盾上限加成",""},
            new[]{"10","額外命數 (Lives)",""},
            new[]{"8","重抽次數 (Rerolls)",""},
            new[]{"7","漂白次數 (Bleaches)",""},
            new[]{"9","放逐次數 (Banishes)",""},
            new[]{"11","跳過次數 (Skips)",""},
            new[]{"1","目前波數 (Wave)","30 以上會進入無盡模式"},
        };
        const int RunMinFields = 43;
        readonly GroupBox grpRun = new GroupBox();
        readonly Label lblRunNone = new Label();
        readonly List<NumericUpDown> runNums = new List<NumericUpDown>();

        string[] RunParts()
        {
            var rs = save == null ? null : save.GetStr("runSave");
            if (string.IsNullOrEmpty(rs)) return null;
            var parts = rs.Split('~');
            if (parts.Length < RunMinFields) return null;
            foreach (var f in RunFields) { int x; if (!int.TryParse(parts[int.Parse(f[0])], NumberStyles.Integer, CultureInfo.InvariantCulture, out x)) return null; }
            return parts;
        }

        void FillRun()
        {
            var parts = RunParts();
            lblRunNone.Visible = parts == null;
            foreach (var n in runNums)
            {
                n.Enabled = parts != null;
                n.Value = parts == null ? 0 : Math.Max(n.Minimum, Math.Min(n.Maximum, int.Parse(parts[int.Parse((string)n.Tag)], CultureInfo.InvariantCulture)));
            }
        }

        void OnRunEdited(object sender, EventArgs e)
        {
            if (loading || save == null) return;
            var parts = RunParts();
            if (parts == null) return;
            var n = (NumericUpDown)sender;
            parts[int.Parse((string)n.Tag)] = ((int)n.Value).ToString(CultureInfo.InvariantCulture);
            save.SetStr("runSave", string.Join("~", parts));
            MarkDirty();
        }

        void SetupNum(NumericUpDown n, string key)
        {
            n.Minimum = 0; n.Maximum = int.MaxValue; n.Width = 160; n.ThousandsSeparator = true;
            n.ValueChanged += (s, e) => { if (loading || save == null) return; save.SetInt(key, (long)n.Value); MarkDirty(); };
        }
        static void AddRow(TableLayoutPanel t, string label, Control c, string hint)
        {
            t.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 6, 12, 6) });
            t.Controls.Add(c);
            t.Controls.Add(new Label { Text = hint, AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(12, 6, 0, 6) });
        }

        // ---- 分頁：解鎖 ----
        TabPage BuildUnlocksTab()
        {
            var p = new TabPage("解鎖");
            var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
            Shown += (s, e) => { try { split.SplitterDistance = (int)(200 * DeviceDpi / 96f); } catch { } };
            lstCats.Dock = DockStyle.Fill; lstCats.IntegralHeight = false;
            foreach (var c in UnlockCats) lstCats.Items.Add(c[1]);
            lstCats.SelectedIndexChanged += (s, e) => FillItems();
            pnlFlags.Dock = DockStyle.Bottom; pnlFlags.AutoSize = true; pnlFlags.FlowDirection = FlowDirection.TopDown; pnlFlags.WrapContents = false; pnlFlags.Padding = new Padding(4);
            pnlFlags.Controls.Add(new Label { Text = "特殊解鎖：", AutoSize = true });
            foreach (var f in FlagKeys)
            {
                var key = f[0];
                var cb = new CheckBox { Text = f[1], AutoSize = true, Tag = key };
                cb.CheckedChanged += (s, e) => { if (loading || save == null) return; save.SetBool(key, cb.Checked); MarkDirty(); };
                pnlFlags.Controls.Add(cb);
            }
            split.Panel1.Controls.Add(lstCats); split.Panel1.Controls.Add(pnlFlags);

            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
            bar.Controls.Add(Btn("全部解鎖", (s, e) => SetAllItems(true)));
            bar.Controls.Add(Btn("全部取消", (s, e) => SetAllItems(false)));
            lstItems.Dock = DockStyle.Fill; lstItems.CheckOnClick = false;   // 第一下只選取（顯示條件），再點一下或按空白鍵才切換 lstItems.IntegralHeight = false;
            lstItems.ItemCheck += OnItemCheck;
            lstItems.SelectedIndexChanged += (s, e) => ShowDetail();
            txtDetail.Dock = DockStyle.Bottom; txtDetail.Height = 70; txtDetail.Multiline = true; txtDetail.ReadOnly = true;
            split.Panel2.Controls.Add(lstItems); split.Panel2.Controls.Add(bar); split.Panel2.Controls.Add(txtDetail);
            p.Controls.Add(split);
            return p;
        }

        string CurCat { get { return lstCats.SelectedIndex < 0 ? null : UnlockCats[lstCats.SelectedIndex][0]; } }

        // 模式×深度：mode 1-6, depth 1-5（深度 X 由上面的旗標控制）
        IEnumerable<string[]> ModeSlots()
        {
            for (int m = 1; m <= 6; m++) for (int d = 1; d <= 5; d++) yield return new[] { m.ToString(), d.ToString() };
        }

        void FillItems()
        {
            loading = true;
            lstItems.BeginUpdate();
            lstItems.Items.Clear();
            txtDetail.Text = "";
            var cat = CurCat;
            if (save != null && cat != null)
            {
                if (cat == "mode")
                {
                    var pl = PairList.Parse(save.GetStr("modeUnlocks"));
                    foreach (var md in ModeSlots())
                    {
                        var a = pl.FindPrefix(md[0], md[1]);
                        lstItems.Items.Add(new ItemRef(md[0] + "," + md[1], Names.Label("mode", md[0]).Replace("#" + md[0] + "  ", "") + " — 深度 " + md[1]), a != null && a.Length > 2 && a[2] != "0");
                    }
                }
                else
                {
                    foreach (var a in PairList.Parse(save.GetStr(cat + "Unlocks")).Items)
                        if (a.Length >= 2) lstItems.Items.Add(new ItemRef(a[0], Names.Label(cat, a[0])), a[1] != "0");
                }
            }
            lstItems.EndUpdate();
            loading = false;
        }

        class ItemRef
        {
            public string Id, Text;
            public ItemRef(string id, string text) { Id = id; Text = text; }
            public override string ToString() { return Text; }
        }

        void OnItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (loading || save == null) return;
            var it = (ItemRef)lstItems.Items[e.Index];
            ApplyUnlock(CurCat, it.Id, e.NewValue == CheckState.Checked);
            MarkDirty();
        }

        void ApplyUnlock(string cat, string id, bool on)
        {
            if (cat == "mode")
            {
                var pl = PairList.Parse(save.GetStr("modeUnlocks"));
                var md = id.Split(',');
                var a = pl.FindPrefix(md[0], md[1]);
                if (a != null) a[2] = on ? "1" : "0";
                else if (on) pl.Items.Add(new[] { md[0], md[1], "1" });
                save.SetStr("modeUnlocks", pl.ToString());
                return;
            }
            var key = cat + "Unlocks";
            var list = PairList.Parse(save.GetStr(key));
            foreach (var a in list.Items) if (a.Length >= 2 && a[0] == id) a[1] = on ? "1" : "0";
            save.SetStr(key, list.ToString());
        }

        void SetAllItems(bool on)
        {
            if (save == null || CurCat == null) return;
            foreach (ItemRef it in lstItems.Items) ApplyUnlock(CurCat, it.Id, on);
            MarkDirty(); FillItems();
        }

        void ShowDetail()
        {
            var it = lstItems.SelectedItem as ItemRef;
            if (it == null || CurCat == null) { txtDetail.Text = ""; return; }
            txtDetail.Text = CurCat == "mode" ? Names.Detail("mode", it.Id.Split(',')[0]) : Names.Detail(CurCat, it.Id);
        }

        // ---- 分頁：研究 ----
        TabPage BuildResearchTab()
        {
            var p = new TabPage("研究");
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
            bar.Controls.Add(Btn("全部升滿", (s, e) => MaxResearch()));
            bar.Controls.Add(Btn("全部歸零", (s, e) => { if (MessageBox.Show(this, "確定把所有研究等級設成 0？", Text, MessageBoxButtons.YesNo) == DialogResult.Yes) SetAllResearch(false); }));
            bar.Controls.Add(new Label { Text = "上限 0 = 沒有上限（可以一直升）", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(12, 8, 0, 0) });
            SetupGrid(gridResearch);
            gridResearch.Columns.Add("tree", "分頁"); gridResearch.Columns.Add("id", "編號");
            gridResearch.Columns.Add("name", "名稱"); gridResearch.Columns.Add("level", "等級"); gridResearch.Columns.Add("max", "上限");
            foreach (DataGridViewColumn c in gridResearch.Columns) c.ReadOnly = c.Name != "level";
            gridResearch.Columns["name"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            gridResearch.CellValueChanged += OnResearchEdited;
            p.Controls.Add(gridResearch); p.Controls.Add(bar);
            return p;
        }

        static void SetupGrid(DataGridView g)
        {
            g.Dock = DockStyle.Fill; g.AllowUserToAddRows = false; g.AllowUserToDeleteRows = false;
            g.RowHeadersVisible = false; g.SelectionMode = DataGridViewSelectionMode.CellSelect;
            g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
            g.BackgroundColor = SystemColors.Window;
        }

        void FillResearch()
        {
            loading = true;
            gridResearch.Rows.Clear();
            if (save != null)
            {
                foreach (var a in PairList.Parse(save.GetStr("researchLevels")).Items)
                {
                    if (a.Length < 3) continue;
                    var tree = Names.Item("researchTree", a[0]);
                    string treeName = tree != null && tree.ContainsKey("zh") ? (string)tree["zh"] : a[0];
                    var it = Names.Item("research", a[0] + "," + a[1]);
                    string nm = it != null ? (string)it["zh"] + (it.ContainsKey("en") ? "  /  " + it["en"] : "") : "?";
                    int max = Names.LevelMax(a[0], a[1]);
                    gridResearch.Rows.Add(a[0] + " " + treeName, a[1], nm, a[2], max < 0 ? "?" : max.ToString());
                    gridResearch.Rows[gridResearch.Rows.Count - 1].Tag = a[0] + "," + a[1];
                }
            }
            loading = false;
        }

        void OnResearchEdited(object sender, DataGridViewCellEventArgs e)
        {
            if (loading || save == null || e.RowIndex < 0 || gridResearch.Columns[e.ColumnIndex].Name != "level") return;
            var row = gridResearch.Rows[e.RowIndex];
            var tid = ((string)row.Tag).Split(',');
            int lv;
            if (!int.TryParse(Convert.ToString(row.Cells["level"].Value), out lv) || lv < 0) lv = 0;
            int max = Names.LevelMax(tid[0], tid[1]);
            if (max > 0 && lv > max) lv = max;
            SetResearch(tid[0], tid[1], lv);
            loading = true; row.Cells["level"].Value = lv.ToString(); loading = false;
            MarkDirty();
        }

        void SetResearch(string tree, string id, int lv)
        {
            var pl = PairList.Parse(save.GetStr("researchLevels"));
            var a = pl.FindPrefix(tree, id);
            if (a != null) a[2] = lv.ToString();
            save.SetStr("researchLevels", pl.ToString());
        }

        void MaxResearch()
        {
            if (save == null) return;
            SetAllResearch(true);
        }

        void SetAllResearch(bool max)
        {
            var pl = PairList.Parse(save.GetStr("researchLevels"));
            foreach (var a in pl.Items)
            {
                if (a.Length < 3) continue;
                int m = Names.LevelMax(a[0], a[1]);
                if (!max) a[2] = "0";
                else if (m > 0) a[2] = m.ToString();   // 無上限 (0) 或未知 (-1) 的項目維持原樣
            }
            save.SetStr("researchLevels", pl.ToString());
            MarkDirty(); FillResearch();
        }

        // ---------- 分頁：即時（遊戲執行中改記憶體） ----------
        // 位址只適用目前這版 GameAssembly.dll；連上時會先核對類別名稱，不符就停用。
        const long PLAYER_TI = 0x3047150, RUN_TI = 0x3057648, TEST_TI = 0x3071698;
        const int K_STATICS = 0xB8, K_CCTOR_DONE = 0xE4;
        const int P_NANOBOT = 0x100;
        const int R_WAVE = 0x8, R_ACTIVE = 0x2A8, R_NANOBOT = 0x390;
        const int T_RUN_INVINCIBLE = 0x31;
        const int NB_INVULN_TIME = 0xC4, NB_ARMOUR = 0xC8, NB_SHIELD = 0xD0, NB_HEALTH = 0xD8;

        readonly Timer liveTimer = new Timer();
        readonly CheckBox chkGod = new CheckBox(), chkGodFull = new CheckBox(), chkLock = new CheckBox();
        readonly Label lblLive = new Label(), lblLiveVals = new Label();
        GameMemory mem;
        bool memBadVersion, godWritten;
        long lastNb;
        int maxHealth, maxShield, maxArmour;

        TabPage BuildLiveTab()
        {
            var p = new TabPage("即時（遊戲中）");
            var f = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(16), AutoScroll = true };
            lblLive.AutoSize = true; lblLive.Font = new Font(Font.FontFamily, 11f, FontStyle.Bold);
            lblLiveVals.AutoSize = true; lblLiveVals.ForeColor = Color.DimGray; lblLiveVals.Margin = new Padding(3, 4, 3, 14);
            chkGod.Text = "無敵（受到攻擊不會掉血、血量歸零也不會死）";
            chkGodFull.Text = "加強無敵：連「碰到礦脈自爆」也擋（飛船會閃爍，會累積「無敵時間」統計）";
            chkLock.Text = "鎖血：血量／護盾／護甲不會減少（以打開時或這局最高的數值為準）";
            foreach (var c in new[] { chkGod, chkGodFull, chkLock }) { c.AutoSize = true; c.Margin = new Padding(3, 6, 3, 6); }
            chkGod.CheckedChanged += (s, e) => { if (!chkGod.Checked) chkGodFull.Checked = false; };
            chkGodFull.CheckedChanged += (s, e) => { if (chkGodFull.Checked) chkGod.Checked = true; };
            chkLock.CheckedChanged += (s, e) => { lastNb = 0; };
            var note = new Label
            {
                AutoSize = true, MaximumSize = new Size(900, 0), ForeColor = Color.DimGray, Margin = new Padding(3, 18, 3, 3),
                Text = "這一頁是在遊戲「開著的時候」直接改記憶體，不用存檔、不用關遊戲，但修改器要一直開著才有效。\r\n" +
                       "關掉修改器或取消勾選就恢復正常。\r\n\r\n" +
                       "無敵擋不住的情況：\r\n" +
                       "・異常事件「裝置充能 +1，波次結束時還有充能就自爆」\r\n" +
                       "・小丑 (Jester) 守護者在第 16 波之後的隨機勝負\r\n" +
                       "・自己在暫停選單放棄這一局\r\n\r\n" +
                       "遊戲更新後位址可能會變，修改器偵測到不符時會自動停用這一頁，不會亂寫。"
            };
            f.Controls.AddRange(new Control[] { lblLive, lblLiveVals, chkGod, chkGodFull, chkLock, note });
            p.Controls.Add(f);
            return p;
        }

        void LiveTick()
        {
            if (mem != null && !mem.Alive) { mem.Dispose(); mem = null; memBadVersion = false; godWritten = false; lastNb = 0; }
            if (mem == null)
            {
                mem = GameMemory.Attach();
                if (mem == null) { SetLive("遊戲沒有在執行", "", Color.DimGray); return; }
                memBadVersion = false;
            }
            if (!memBadVersion)
            {
                long pk = Klass(PLAYER_TI), rk = Klass(RUN_TI), tk = Klass(TEST_TI);
                if (pk == 0 || rk == 0 || tk == 0) { SetLive("遊戲載入中…", "", Color.DimGray); return; }
                if (mem.KlassName(pk) != "Player" || mem.KlassName(rk) != "Run" || mem.KlassName(tk) != "Test") memBadVersion = true;
            }
            if (memBadVersion) { SetLive("⚠ 遊戲版本和修改器不符，即時功能已停用", "遊戲可能更新過了，需要重新分析位址。", Color.Firebrick); return; }

            long pS = Statics(PLAYER_TI), rS = Statics(RUN_TI), tS = Statics(TEST_TI);
            if (pS == 0 || rS == 0 || tS == 0) { SetLive("遊戲載入中…", "", Color.DimGray); return; }

            // 無敵：每一格都重寫（遊戲的 Test.Awake 會把它歸零）
            if (chkGod.Checked) { mem.SetBool(tS + T_RUN_INVINCIBLE, true); godWritten = true; }
            else if (godWritten) { mem.SetBool(tS + T_RUN_INVINCIBLE, false); godWritten = false; }

            // 只有在局內、且 Player.nanobot == Run.nanobot 時才動飛船數值（選單裡的是預覽用的假飛船）
            long nb = mem.Ptr(rS + R_NANOBOT);
            bool inRun = mem.Bool(rS + R_ACTIVE) == true && nb != 0 && nb == mem.Ptr(pS + P_NANOBOT) && mem.ClassName(nb) == "Nanobot";
            if (!inRun) { lastNb = 0; SetLive("已連接遊戲（目前不在局內）", chkGod.Checked ? "無敵已開啟，開始一局後就會生效。" : "", Color.SeaGreen); return; }

            if (chkGodFull.Checked && (mem.Float(nb + NB_INVULN_TIME) ?? 0f) < 2f) mem.SetFloat(nb + NB_INVULN_TIME, 3f);

            int h = mem.Int(nb + NB_HEALTH) ?? 0, sh = mem.Int(nb + NB_SHIELD) ?? 0, ar = mem.Int(nb + NB_ARMOUR) ?? 0;
            if (chkLock.Checked)
            {
                if (nb != lastNb) { lastNb = nb; maxHealth = h; maxShield = sh; maxArmour = ar; }
                maxHealth = Math.Max(maxHealth, h); maxShield = Math.Max(maxShield, sh); maxArmour = Math.Max(maxArmour, ar);
                if (h < maxHealth) mem.SetInt(nb + NB_HEALTH, h = maxHealth);
                if (sh < maxShield) mem.SetInt(nb + NB_SHIELD, sh = maxShield);
                if (ar < maxArmour) mem.SetInt(nb + NB_ARMOUR, ar = maxArmour);
            }
            int wave = mem.Int(rS + R_WAVE) ?? 0;
            SetLive("已連接遊戲：第 " + wave + " 波" + (chkGod.Checked ? "  —  無敵中" : ""),
                    "血量 " + h + "　護盾 " + sh + "　護甲 " + ar, chkGod.Checked ? Color.RoyalBlue : Color.SeaGreen);
        }

        long Klass(long rva) { return mem.Ptr(mem.GameAssembly + rva); }
        long Statics(long rva)
        {
            long k = Klass(rva);
            if (k == 0 || (mem.Int(k + K_CCTOR_DONE) ?? 0) == 0) return 0;
            return mem.Ptr(k + K_STATICS);
        }

        void SetLive(string status, string vals, Color c)
        {
            if (lblLive.Text != status) lblLive.Text = status;
            if (lblLiveVals.Text != vals) lblLiveVals.Text = vals;
            lblLive.ForeColor = c;
        }

        void LiveShutdown()
        {
            liveTimer.Stop();
            try
            {
                if (mem != null && godWritten && !memBadVersion)
                {
                    long tS = Statics(TEST_TI);
                    if (tS != 0) mem.SetBool(tS + T_RUN_INVINCIBLE, false);
                }
            }
            catch { }
            if (mem != null) mem.Dispose();
        }

        // ---- 分頁：進階（全部數值） ----
        TabPage BuildAllTab()
        {
            var p = new TabPage("進階（全部數值）");
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
            bar.Controls.Add(new Label { Text = "搜尋：", AutoSize = true, Margin = new Padding(4, 8, 0, 0) });
            txtFilter.Width = 240; txtFilter.TextChanged += (s, e) => FillAll();
            bar.Controls.Add(txtFilter);
            bar.Controls.Add(new Label { Text = "直接編輯「值」欄；布林值填 true / false。", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(12, 8, 0, 0) });
            SetupGrid(gridAll);
            gridAll.Columns.Add("sec", "類型"); gridAll.Columns.Add("key", "鍵"); gridAll.Columns.Add("val", "值");
            gridAll.Columns["sec"].ReadOnly = true; gridAll.Columns["key"].ReadOnly = true;
            gridAll.Columns["val"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            gridAll.Columns["val"].DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            gridAll.CellValueChanged += OnAllEdited;
            p.Controls.Add(gridAll); p.Controls.Add(bar);
            return p;
        }

        // 從遊戲讀檔程式確認過的危險欄位
        static string DangerWarning(string key)
        {
            switch (key)
            {
                case "fresh": case "build": return "設成 true／改掉後，遊戲會把整個存檔當成新玩家重置。";
                case "dev": return "會打開開發者主控台，裡面的指令可以清掉你 Steam 帳號上所有成就。";
                case "modeId": return "只能是 1～6，其他值會讓遊戲讀檔失敗並改讀備份（修改全部失效）。";
                case "saveEpoch": return "必須大於 0，否則遊戲會判定存檔損壞。修改器儲存時會自動處理這個值。";
                case "version": return "改了會讓遊戲執行舊版存檔的轉換程序，可能弄亂資料。";
                case "storyState": case "storyStateSub": case "storyCompletedOnce": return "會讓劇情進度跳躍。";
                case "runSave": return "格式很嚴格（43 個以上以 ~ 分隔的欄位），寫錯會讓這一局被重置。建議改用「資源」分頁的「目前這一局」。";
            }
            if (key.StartsWith("nanobot")) return "每個欄位都必須是數字（武器 DNA 欄除外），寫錯會讓遊戲讀檔時出錯。";
            return null;
        }

        static string SecName(string s)
        {
            switch (s) { case "StringData": return "文字"; case "IntData": return "整數"; case "FloatData": return "小數"; case "BoolData": return "布林"; default: return s; }
        }

        void FillAll()
        {
            loading = true;
            gridAll.Rows.Clear();
            if (save != null)
            {
                string f = txtFilter.Text.Trim();
                var rows = new List<DataGridViewRow>();
                foreach (var sec in save.Order)
                    foreach (var e in save.Sections[sec])
                    {
                        if (f.Length > 0 && e.Key.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        var r = new DataGridViewRow();
                        r.CreateCells(gridAll, SecName(sec), e.Key, FormatVal(e.Value));
                        r.Tag = e; rows.Add(r);
                    }
                gridAll.Rows.AddRange(rows.ToArray());
            }
            loading = false;
        }

        static string FormatVal(object v)
        {
            if (v is bool) return (bool)v ? "true" : "false";
            if (v is decimal) return ((decimal)v).ToString(CultureInfo.InvariantCulture);
            if (v is double) return ((double)v).ToString("R", CultureInfo.InvariantCulture);
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        void OnAllEdited(object sender, DataGridViewCellEventArgs e)
        {
            if (loading || save == null || e.RowIndex < 0) return;
            var row = gridAll.Rows[e.RowIndex];
            var en = (Entry)row.Tag;
            string txt = Convert.ToString(row.Cells["val"].Value) ?? "";
            object nv = null;
            if (en.Value is bool)
            {
                var t = txt.Trim().ToLowerInvariant();
                if (t == "true" || t == "1") nv = true; else if (t == "false" || t == "0") nv = false;
            }
            else if (en.Value is string) nv = txt;
            else if (en.Value is int || en.Value is long)
            {
                int iv; if (int.TryParse(txt.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out iv)) nv = iv;
            }
            else
            {
                double dv; if (double.TryParse(txt.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out dv) && !double.IsNaN(dv) && !double.IsInfinity(dv)) nv = dv;
            }
            if (nv == null)
            {
                MessageBox.Show(this, "「" + txt + "」不是這個欄位能用的值，已還原。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                loading = true; row.Cells["val"].Value = FormatVal(en.Value); loading = false;
                return;
            }
            string warn = DangerWarning(en.Key);
            if (warn != null && MessageBox.Show(this, "⚠ " + warn + "\r\n\r\n確定要改「" + en.Key + "」嗎？", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                loading = true; row.Cells["val"].Value = FormatVal(en.Value); loading = false;
                return;
            }
            en.Value = nv;
            MarkDirty();
        }

        // ---- 共用 ----
        void RefreshTab()
        {
            if (save == null) return;
            loading = true;
            numResearch.Value = Clamp(save.GetInt("research"));
            numHammers.Value = Clamp(save.GetInt("hammers"));
            FillRun();
            foreach (Control c in pnlFlags.Controls)
            {
                var cb = c as CheckBox;
                if (cb != null) { cb.Enabled = save.HasBool((string)cb.Tag); cb.Checked = save.GetBool((string)cb.Tag); }
            }
            loading = false;
            if (tabs.SelectedIndex == 1) { if (lstCats.SelectedIndex < 0) lstCats.SelectedIndex = 0; else FillItems(); }
            else if (tabs.SelectedIndex == 2) FillResearch();
            else if (tabs.SelectedIndex == 3) FillAll();
        }
        static decimal Clamp(long v) { return Math.Max(0, Math.Min(int.MaxValue, v)); }

        void MarkDirty() { dirty = true; UpdateStatus(); }

        static bool GameRunning()
        {
            try { return Process.GetProcessesByName("Genome Guardian 2").Length > 0; } catch { return false; }
        }

        void UpdateStatus()
        {
            if (GameRunning()) { lblStatus.Text = "⚠ 遊戲正在執行：請先關閉遊戲再儲存"; lblStatus.ForeColor = Color.Firebrick; }
            else if (dirty) { lblStatus.Text = "● 有未儲存的修改"; lblStatus.ForeColor = Color.DarkOrange; }
            else { lblStatus.Text = save == null ? "" : "✓ 已和遊戲存檔同步"; lblStatus.ForeColor = Color.SeaGreen; }
        }

        bool ConfirmDiscard()
        {
            if (!dirty) return true;
            return MessageBox.Show(this, "有修改還沒儲存，確定要放棄嗎？", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        string MainPath { get { return Path.Combine(SaveDir, SaveFiles[0]); } }
        DateTime loadedStamp;

        // 遊戲存檔後自動重新讀取（只在沒有未儲存修改時）
        void AutoReload()
        {
            if (save == null || dirty) return;
            try
            {
                var stamp = File.GetLastWriteTimeUtc(MainPath);
                if (stamp == loadedStamp) return;
                var s = SaveData.Load(MainPath);   // 遊戲寫到一半時會失敗，下次再試
                save = s; loadedStamp = stamp;
                lblPath.Text = "存檔：" + MainPath + "    （saveEpoch " + save.GetInt("saveEpoch") + "，已自動更新）";
                RefreshTab();
            }
            catch { }
        }

        void LoadSave()
        {
            try
            {
                if (!File.Exists(MainPath)) throw new FileNotFoundException("找不到存檔：" + MainPath + "\r\n請先開過一次遊戲。");
                loadedStamp = File.GetLastWriteTimeUtc(MainPath);
                save = SaveData.Load(MainPath);
                dirty = false;
                lblPath.Text = "存檔：" + MainPath + "    （saveEpoch " + save.GetInt("saveEpoch") + "）";
                RefreshTab();
            }
            catch (Exception ex)
            {
                save = null;
                MessageBox.Show(this, "讀取存檔失敗：\r\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            UpdateStatus();
        }

        string BackupNow(string tag)
        {
            string dir = Path.Combine(BackupRoot, DateTime.Now.ToString("yyyy-MM-dd_HHmmss") + tag);
            Directory.CreateDirectory(dir);
            foreach (var f in SaveFiles)
            {
                var src = Path.Combine(SaveDir, f);
                if (File.Exists(src)) File.Copy(src, Path.Combine(dir, f), true);
            }
            return dir;
        }

        void WriteAll(byte[] data)
        {
            foreach (var f in SaveFiles)
            {
                var dst = Path.Combine(SaveDir, f);
                var tmp = dst + ".gg2edit.tmp";
                File.WriteAllBytes(tmp, data);
                if (File.Exists(dst)) File.Replace(tmp, dst, null); else File.Move(tmp, dst);
            }
        }

        void DoSave()
        {
            if (save == null) return;
            if (GameRunning())
            {
                MessageBox.Show(this, "遊戲還開著。遊戲關閉時會用它記憶體裡的資料覆蓋存檔，修改會消失。\r\n請先完全關閉 Genome Guardian 2 再按儲存。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                // 存檔在修改器開著的期間被遊戲改過 → 先問
                var disk = SaveData.Load(MainPath);
                if (disk.GetInt("saveEpoch") != save.GetInt("saveEpoch") &&
                    MessageBox.Show(this, "遊戲在你開啟修改器之後又存過檔了（可能剛玩過）。\r\n繼續儲存會用修改器裡的舊資料蓋掉那些新進度。\r\n\r\n建議按「否」，再按「重新讀取」重做修改。要硬蓋嗎？", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;

                string dir = BackupNow("");
                save.SetInt("saveEpoch", Math.Max(save.GetInt("saveEpoch"), disk.GetInt("saveEpoch")) + 1);
                byte[] data = SaveData.Xor(Encoding.UTF8.GetBytes(save.ToJson()));
                SaveData.Load(VerifyTemp(data)); // 寫入前先確認能被正確讀回
                WriteAll(data);
                loadedStamp = File.GetLastWriteTimeUtc(MainPath);
                dirty = false;
                lblPath.Text = "存檔：" + MainPath + "    （saveEpoch " + save.GetInt("saveEpoch") + "）";
                UpdateStatus();
                MessageBox.Show(this, "已儲存！開遊戲就能看到效果。\r\n\r\n原本的存檔備份在：\r\n" + dir, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "儲存失敗，存檔沒有被改動：\r\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        static string VerifyTemp(byte[] data)
        {
            var p = Path.Combine(Path.GetTempPath(), "gg2edit_verify.txt");
            File.WriteAllBytes(p, data);
            return p;
        }

        void UnlockEverything()
        {
            if (save == null) return;
            if (MessageBox.Show(this, "會解鎖：所有守護者、零件、裝置、模組、塗裝、圖案、游標/準星/瞄準錐、所有模式與深度、特殊解鎖，並把研究升到上限。\r\n（還沒按「儲存到遊戲」之前都不會真的寫入）\r\n\r\n⚠ 注意：遊戲讀檔時會檢查成就，解鎖的東西可能會讓你的 Steam 帳號直接解開對應的成就，這個無法從修改器還原。\r\n\r\n確定？", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            UnlockAllCore();
            MarkDirty(); RefreshTab();
        }

        void UnlockAllCore()
        {
            foreach (var c in UnlockCats)
            {
                if (c[0] == "mode") { foreach (var md in ModeSlots()) ApplyUnlock("mode", md[0] + "," + md[1], true); continue; }
                var pl = PairList.Parse(save.GetStr(c[0] + "Unlocks"));
                foreach (var a in pl.Items) if (a.Length >= 2) a[1] = "1";
                save.SetStr(c[0] + "Unlocks", pl.ToString());
            }
            foreach (var f in FlagKeys) save.SetBool(f[0], true);
            SetAllResearch(true);
        }

        void RestoreBackup()
        {
            if (GameRunning()) { MessageBox.Show(this, "請先關閉遊戲再還原。", Text); return; }
            Directory.CreateDirectory(BackupRoot);
            using (var dlg = new FolderBrowserDialog { SelectedPath = BackupRoot, Description = "選擇要還原的備份資料夾（裡面有 genome-guardian-2.txt）", ShowNewFolderButton = false })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var src = Path.Combine(dlg.SelectedPath, SaveFiles[0]);
                if (!File.Exists(src)) { MessageBox.Show(this, "這個資料夾裡沒有 " + SaveFiles[0], Text); return; }
                try
                {
                    SaveData.Load(src);
                    string dir = BackupNow("_還原前");
                    foreach (var f in SaveFiles)
                    {
                        var s = Path.Combine(dlg.SelectedPath, f);
                        File.Copy(File.Exists(s) ? s : src, Path.Combine(SaveDir, f), true);
                    }
                    dirty = false; LoadSave();
                    MessageBox.Show(this, "已還原。還原前的存檔也另外備份在：\r\n" + dir, Text);
                }
                catch (Exception ex) { MessageBox.Show(this, "還原失敗：" + ex.Message, Text); }
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern bool AttachConsole(int pid);

        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length > 0) { try { AttachConsole(-1); } catch { } }   // 命令列參數模式：輸出到呼叫它的命令列
            if (args.Length == 2 && args[0] == "--roundtrip")
            {
                // 測試用：讀取 → 重新序列化，輸出是否與原檔相同
                var raw = File.ReadAllBytes(args[1]);
                var s = SaveData.Load(args[1]);
                var outb = SaveData.Xor(Encoding.UTF8.GetBytes(s.ToJson()));
                Console.WriteLine(raw.SequenceEqual(outb) ? "IDENTICAL" : "DIFFERENT " + raw.Length + " vs " + outb.Length);
                File.WriteAllBytes(args[1] + ".rt", outb);
                return;
            }
            Names.Init();
            if (args.Length == 1 && args[0] == "--liveprobe")
            {
                // 測試用：唯讀連接遊戲並顯示狀態（所有勾選都是關的 → 不會寫入記憶體）
                var f = new MainForm();
                for (int i = 0; i < 3; i++) { f.LiveTick(); Console.WriteLine(f.lblLive.Text + " | " + f.lblLiveVals.Text); System.Threading.Thread.Sleep(300); }
                if (f.mem != null) f.mem.Dispose();
                return;
            }
            if (args.Length == 3 && args[0] == "--selftest")
            {
                // 測試用：對副本執行「一鍵全解鎖」並輸出
                var f = new MainForm();
                f.save = SaveData.Load(args[1]);
                f.UnlockAllCore();
                f.FillRun();
                if (f.runNums[0].Enabled) { f.runNums[0].Value = 999; f.runNums[1].Value = 77; }
                File.WriteAllBytes(args[2], SaveData.Xor(Encoding.UTF8.GetBytes(f.save.ToJson())));
                Console.WriteLine("OK");
                return;
            }
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
