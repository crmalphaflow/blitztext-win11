using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace BlitztextWindows;

public partial class GhostWindow : Window
{
    public event EventHandler? RestoreRequested;

    public GhostWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => MoveToBottomRight();
    }

    public void SetState(AppVisualState state)
    {
        var color = state switch
        {
            AppVisualState.Recording => "#DC2626",
            AppVisualState.Processing => "#D97706",
            AppVisualState.Success => "#16A34A",
            AppVisualState.Error => "#DC2626",
            _ => "#16A34A"
        };

        Shell.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
    }

    private void MoveToBottomRight()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 22;
        Top = workArea.Bottom - Height - 22;
    }

    private void Window_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        RestoreRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }
}
