namespace ShiftFlow.Mobile;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();

        NavigationPage.SetHasNavigationBar(this, false);
        ShiftFlowWebView.Navigating += (_, _) => LoadingIndicator.IsVisible = true;
        ShiftFlowWebView.Navigated += (_, _) => LoadingIndicator.IsVisible = false;
        ShiftFlowWebView.Source = ShiftFlowServerConfig.ResolveUrl();
    }
}
