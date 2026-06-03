using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace ScourgifyMini
{
    /// <summary>
    /// Interaction logic for AboutWindow.xaml
    /// </summary>
    public partial class AboutWindow : Window
    {
        public string Version { get; set; }

        public AboutWindow()
        {
            InitializeComponent();

            Version = $"v{Assembly.GetExecutingAssembly().GetName().Version}";

            DataContext = this;
            RefreshLocalizedText();
        }

        public void RefreshLocalizedText()
        {
            Title = Properties.Resources.About;
            AboutDescriptionTextBlock.Text = Properties.Resources.AboutDescription;
            DevelopedByLabel.Text = Properties.Resources.DevelopedBy;
            LicenseLabel.Text = Properties.Resources.License;
            ProjectPageLabel.Text = Properties.Resources.ProjectPage;
            AllRightsReservedTextBlock.Text = Properties.Resources.AllRightsReserved;
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
            e.Handled = true;
        }
    }
}
