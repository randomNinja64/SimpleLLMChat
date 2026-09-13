using System;
using System.Drawing;
using System.Windows.Forms;

namespace DesktopTools.Fixture
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new FixtureForm());
        }
    }

    public class FixtureForm : Form
    {
        readonly Label _status;
        Point _dragStart;
        bool _dragging;

        public FixtureForm()
        {
            Text = "DesktopTools.XpFixture";
            Name = "frmFixture";
            AccessibleName = "DesktopTools.XpFixture";
            Width = 520;
            Height = 480;
            StartPosition = FormStartPosition.CenterScreen;

            _status = new Label
            {
                Name = "lblStatus",
                AccessibleName = "lblStatus",
                Text = "status:ready",
                AutoSize = true,
                Location = new Point(20, 12)
            };

            Button btn = new Button
            {
                Name = "btnClick",
                AccessibleName = "btnClick",
                Text = "Click Me",
                Location = new Point(20, 40),
                Size = new Size(120, 32)
            };
            // Invoke/BM_CLICK/accDoDefaultAction can fire Click without moving Win32
            // focus. Take focus here so later navigate.focus on txtEdit still raises GotFocus.
            btn.Click += (s, e) =>
            {
                btn.Focus();
                SetStatus("clicked");
            };

            Label dbl = new Label
            {
                Name = "lblDouble",
                AccessibleName = "lblDouble",
                Text = "DoubleClick Me",
                Location = new Point(160, 44),
                AutoSize = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.LightYellow
            };
            dbl.DoubleClick += (s, e) => SetStatus("double_clicked");
            dbl.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Right)
                    SetStatus("context");
            };
            var context = new ContextMenu();
            context.MenuItems.Add("Ping", (s, e) => SetStatus("context"));
            dbl.ContextMenu = context;

            TextBox edit = new TextBox
            {
                Name = "txtEdit",
                AccessibleName = "txtEdit",
                Location = new Point(20, 90),
                Size = new Size(300, 24),
                Text = "initial"
            };
            edit.GotFocus += (s, e) => SetStatus("focused");

            RadioButton radio = new RadioButton
            {
                Name = "rdoChoice",
                AccessibleName = "rdoChoice",
                Text = "Choice A",
                Location = new Point(160, 128),
                AutoSize = true
            };
            radio.CheckedChanged += (s, e) =>
            {
                if (radio.Checked)
                    SetStatus("radio");
            };

            CheckBox check = new CheckBox
            {
                Name = "chkOption",
                AccessibleName = "chkOption",
                Text = "Option",
                Location = new Point(20, 130),
                AutoSize = true
            };
            ListBox virtualList = null;
            ComboBox combo = null;
            check.CheckedChanged += (s, e) =>
            {
                if (check.Checked)
                {
                    combo = new ComboBox
                    {
                        Name = "cboItems",
                        AccessibleName = "cboItems",
                        DropDownStyle = ComboBoxStyle.DropDownList,
                        Location = new Point(330, 40),
                        Size = new Size(160, 21)
                    };
                    combo.Items.Add("combo-alpha");
                    combo.Items.Add("combo-beta");
                    combo.SelectedIndex = 0;
                    combo.SelectedIndexChanged += (cs, ce) =>
                    {
                        if (combo.SelectedItem != null &&
                            combo.SelectedItem.ToString() == "combo-beta")
                            SetStatus("combo");
                    };
                    Controls.Add(combo);

                    virtualList = new ListBox
                    {
                        Name = "virtualList",
                        AccessibleName = "virtualList",
                        Location = new Point(330, 90),
                        Size = new Size(160, 70)
                    };
                    virtualList.Items.Add("virtual-item-one");
                    virtualList.Items.Add("virtual-item-two");
                    virtualList.SelectedIndexChanged += (ls, le) =>
                    {
                        if (virtualList.SelectedIndex >= 0)
                            SetStatus("list_item");
                    };
                    Controls.Add(virtualList);
                    virtualList.BringToFront();
                }
                else
                {
                    if (virtualList != null)
                    {
                        virtualList.Dispose();
                        virtualList = null;
                    }
                    if (combo != null)
                    {
                        combo.Dispose();
                        combo = null;
                    }
                }
            };

            Panel dragPanel = new Panel
            {
                Name = "pnlDrag",
                AccessibleName = "pnlDrag",
                Location = new Point(20, 170),
                Size = new Size(200, 100),
                BackColor = Color.LightSteelBlue,
                BorderStyle = BorderStyle.FixedSingle
            };
            Label dragHint = new Label
            {
                Name = "lblDragHint",
                AccessibleName = "lblDragHint",
                Text = "Drag Me",
                AutoSize = true,
                Location = new Point(8, 8)
            };
            dragPanel.Controls.Add(dragHint);
            dragPanel.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    _dragging = true;
                    _dragStart = e.Location;
                }
            };
            dragPanel.MouseMove += (s, e) =>
            {
                if (!_dragging || e.Button != MouseButtons.Left)
                    return;
                int dx = Math.Abs(e.X - _dragStart.X);
                int dy = Math.Abs(e.Y - _dragStart.Y);
                if (dx + dy >= 8)
                    SetStatus("dragged");
            };
            dragPanel.MouseUp += (s, e) => { _dragging = false; };

            Panel dropPanel = new Panel
            {
                Name = "pnlDrop",
                AccessibleName = "pnlDrop",
                Location = new Point(240, 170),
                Size = new Size(200, 100),
                BackColor = Color.Honeydew,
                BorderStyle = BorderStyle.FixedSingle
            };
            dropPanel.Controls.Add(new Label
            {
                Name = "lblDropHint",
                AccessibleName = "lblDropHint",
                Text = "Drop Here",
                AutoSize = true,
                Location = new Point(8, 8)
            });

            Panel scrollHost = new Panel
            {
                Name = "pnlScroll",
                AccessibleName = "pnlScroll",
                Location = new Point(20, 290),
                Size = new Size(420, 120),
                AutoScroll = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White
            };
            Panel scrollContent = new Panel
            {
                Name = "pnlScrollContent",
                AccessibleName = "pnlScrollContent",
                Location = new Point(0, 0),
                Size = new Size(400, 600),
                BackColor = Color.WhiteSmoke
            };
            for (int i = 0; i < 20; i++)
            {
                scrollContent.Controls.Add(new Label
                {
                    Name = "lblScroll" + i,
                    AccessibleName = "scroll-line-" + i,
                    Text = "scroll-line-" + i,
                    AutoSize = true,
                    Location = new Point(8, 8 + i * 28)
                });
            }
            scrollHost.Controls.Add(scrollContent);
            scrollHost.Scroll += (s, e) => SetStatus("scrolled");
            scrollHost.MouseWheel += (s, e) => SetStatus("scrolled");

            Controls.Add(_status);
            Controls.Add(btn);
            Controls.Add(dbl);
            Controls.Add(edit);
            Controls.Add(check);
            Controls.Add(radio);
            Controls.Add(dragPanel);
            Controls.Add(dropPanel);
            Controls.Add(scrollHost);
        }

        void SetStatus(string value)
        {
            _status.Text = "status:" + value;
            // Keep a stable title prefix so list_windows still matches FixtureTitle.
            Text = "DesktopTools.XpFixture - " + value;
        }
    }
}
