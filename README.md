# NoesisGUIBindingsGenerator
A simple piece of code that helps generate .g.cs files for Unity3d

It'll pump out code that looks like this:
```csharp
using Noesis;

namespace Assets.UI.Views.DesignerUI {
	public partial class DesignerMain : UserControl {
		internal Grid JustNormalGrid;
		internal UniformGrid MyUniformGrid;
		internal Assets.UI.Views.DesignerUI.CircleButton Skinkeskinke;
    
		private void InitializeComponent() {
			GUI.LoadComponent(this, "Assets/UI/Views/DesignerUI/DesignerMain.xaml");
			this.JustNormalGrid = (Grid)FindName("JustNormalGrid");
			this.MyUniformGrid = (UniformGrid)FindName("MyUniformGrid");
			this.Skinkeskinke = (Assets.UI.Views.DesignerUI.CircleButton)FindName("Skinkeskinke");
		}
	}
}
```

Installation:
In NoesisPostprocessor.cs find a method called ScanDependencies() in the bottom of the method body paste this code: "NoesisCsBindingsGenerator.GenerateCsFile(filename);" and you're good to go.

Forum thread: https://www.noesisengine.com/forums/viewtopic.php?f=3&t=1670
