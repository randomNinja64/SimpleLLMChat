using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace SimpleLLMChatGUI
{
    /// <summary>
    /// Asks whether to run a tool call. Replaces a message box, which grows with its
    /// text and pushes its buttons off-screen when a call carries a large file body:
    /// here the arguments scroll and the window never outgrows the screen it opens on.
    /// </summary>
    public partial class ApprovalDialog : Window
    {
        // Share of the screen's work area the dialog may take up when it sizes itself.
        private const double MaxScreenFraction = 0.8;

        private ApprovalDialog(string toolName, string arguments, string confidence)
        {
            InitializeComponent();
            FontHandler.ApplyFontToWindow(this);
            int fontSize = FontHandler.GetFontSize();
            FontSize = fontSize;
            ArgumentsText.FontSize = fontSize;
            ArgumentsText.SetResourceReference(FontFamilyProperty, FontHandler.CodeBlockFontFamilyKey);

            ToolNameText.Text = ToolApproval.RunToolPrefix + toolName;
            if (!string.IsNullOrEmpty(confidence))
            {
                ConfidenceText.Text = ToolApproval.ConfidencePrefix + confidence;
                ConfidenceText.Visibility = Visibility.Visible;
            }
            ArgumentsText.Text = ToolArgumentsFormatter.Format(arguments);
        }

        /// <summary>Shows the dialog; true when the user approves the call.</summary>
        public static bool Ask(Window owner, string toolName, string arguments, string confidence)
        {
            var dialog = new ApprovalDialog(toolName, arguments, confidence) { Owner = owner };
            dialog.LimitToScreen(dialog.ScreenWorkArea());
            return dialog.ShowDialog() == true;
        }

        private void LimitToScreen(Rect workArea)
        {
            MaxWidth = Math.Max(MinWidth, workArea.Width * MaxScreenFraction);
            MaxHeight = Math.Max(MinHeight, workArea.Height * MaxScreenFraction);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Centred on its owner, the dialog can still hang off the screen when the
            // owner sits near an edge: pull it back inside the work area.
            Rect workArea = ScreenWorkArea();
            Left = Math.Max(workArea.Left, Math.Min(Left, workArea.Right - ActualWidth));
            Top = Math.Max(workArea.Top, Math.Min(Top, workArea.Bottom - ActualHeight));

            ArgumentsText.ScrollToHome();
            NoButton.Focus();
        }

        /// <summary>
        /// The work area (screen minus taskbar) of the monitor showing the owner, in
        /// WPF units; the primary screen's when there is no owner.
        /// </summary>
        private Rect ScreenWorkArea()
        {
            Window reference = Owner ?? this;
            IntPtr handle = new WindowInteropHelper(reference).Handle;
            Forms.Screen screen = handle != IntPtr.Zero
                ? Forms.Screen.FromHandle(handle)
                : Forms.Screen.PrimaryScreen;
            System.Drawing.Rectangle area = screen.WorkingArea;

            // Screen reports device pixels; WPF positions in 96-dpi units.
            Matrix fromDevice = Matrix.Identity;
            PresentationSource source = PresentationSource.FromVisual(reference);
            if (source != null && source.CompositionTarget != null)
                fromDevice = source.CompositionTarget.TransformFromDevice;

            Point topLeft = fromDevice.Transform(new Point(area.Left, area.Top));
            Point bottomRight = fromDevice.Transform(new Point(area.Right, area.Bottom));
            return new Rect(topLeft, bottomRight);
        }

        private void YesButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
