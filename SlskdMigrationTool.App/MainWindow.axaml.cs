using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;

namespace SlskdMigrationTool.App;

public partial class MainWindow : Window
{
    public readonly Action ExceptionThrown;
    public readonly Action LoadingFinished;
    public readonly Action LoadingStarted;
    
    public MainWindow()
    {
        InitializeComponent();
        
        FlexiblePanel.Children.Add(new HomePanel()
        {
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        });

        FlexiblePanel.Children.CollectionChanged += (_, _) =>
        {
            if (FlexiblePanel.Children.FirstOrDefault() is UserControl child)
                MainMenuButton.IsVisible = child.GetType() != typeof(HomePanel);
        };
        
        LoadingStarted += () =>
        {
            ReadyIndicator.IsVisible = false;
            LoadingIndicator.IsVisible = true;
        };

        LoadingFinished += () =>
        {
            LoadingIndicator.IsVisible = false;
            ReadyIndicator.IsVisible = true;
        };
        
        ExceptionThrown += async () =>
        {
            ErrorIndicatorText.Text = "Exception thrown!";

            ReadyIndicator.IsVisible = false;
            ErrorIndicator.IsVisible = true;

            var animation = (Animation)App.Current.Resources["ErrorAnimation"] ?? throw new NullReferenceException();
            await animation.RunAsync(ErrorIndicatorIcon);

            await Task.Delay(5000);

            ErrorIndicator.IsVisible = false;
            ReadyIndicator.IsVisible = true;
        };
    }
    
    private void MainMenuButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var homePanel = new HomePanel();

        FlexiblePanel.Children.Clear();
        FlexiblePanel.Children.Add(homePanel);
    }
}