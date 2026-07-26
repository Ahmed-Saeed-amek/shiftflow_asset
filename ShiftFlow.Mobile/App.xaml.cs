using Microsoft.Extensions.DependencyInjection;

namespace ShiftFlow.Mobile;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		// No Shell here on purpose: this app is a single full-screen WebView
		// shell around ShiftFlow.Web, with no flyout/tabs/routing to justify
		// Shell's overhead — and MAUI's ShellItemRenderer crashes on this
		// Android/Material Components combo (BottomNavigationView requires a
		// TextAppearance the default theme doesn't provide). A bare
		// ContentPage as the window root also crashes on Android (its
		// internal NavigationRootManager expects view IDs NavigationPage/Shell
		// normally provide) — NavigationPage is the minimal working root.
		return new Window(new NavigationPage(new MainPage()));
	}
}