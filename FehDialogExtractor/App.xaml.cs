using System.Windows;

namespace FehDialogExtractor
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        // Note: MainWindow initializes theme in its constructor before InitializeComponent,
        // so we keep App.xaml with StartupUri to allow normal startup.
    }

}
