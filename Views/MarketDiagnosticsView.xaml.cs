using System.Windows;
using System.Windows.Controls;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;

namespace CrossETF.Terminal.UiShell.Reference.Views;

public partial class MarketDiagnosticsView : UserControl
{
    private readonly MarketDiagnosticsSnapshotService _snapshotService;

    public MarketDiagnosticsView(MarketDiagnosticsSnapshotService snapshotService)
    {
        _snapshotService = snapshotService;
        InitializeComponent();
        RefreshLocalState();
    }

    public MarketDiagnosticsSnapshot? Snapshot { get; private set; }

    public void RefreshLocalState()
    {
        Snapshot = _snapshotService.BuildSnapshot();
        DataContext = Snapshot;
        DiagnosticsStatusText.Text = "已重新读取本地状态";
    }

    private void ReloadLocalStateButton_Click(object sender, RoutedEventArgs e)
        => RefreshLocalState();
}
