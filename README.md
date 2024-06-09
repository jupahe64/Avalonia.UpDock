# What's UpDock?
nothing much...

*...except the best Docking library you'll ever use or alteast it will be.*

![image](https://github.com/jupahe64/Avalonia.UpDock/assets/33004544/3eac7d0c-4220-4b9f-9dd4-461b1c634cd4)


## Why?
Because the two other Dock libraries that exists either have on outdated style or just don't work reliably.  
This library aims to work with any style, theme and whatnot by simply using or slightly extending the functionality of existing controls and hocking into a few UI events and ItemTemplates.  
This should in theory also make this library future proof.

## Classes/Controls

- `DockingManager` with minimal setup
- `SplitPanel` to host the docking slots
- `DockingTabControl` to host the DockingTabs

## Features
- Rearrange Tabs
- Close Tabs
- Drag Tab out of TabControl to create a new floating window
- Drag Tab into another TabControl, that's part of the same docking host (DockingManager), to add it as a tab
- Drag Tab into another TabControl to create a new split and Dock it there

## Getting started
To use the DockingManager you need to have a docking host control that fits the following requirements:
1. The host itself is a SplitPanel
2. All children should be either a SplitPanel or a DockingTabControl (you are still allowed to use other controls, they just wont support docking)
3. All children that are SplitPanels must have the opposite `Orientation` of their Parent and follow the requirements **2** and **3**

### An Example

MainWindow.axaml
```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        mc:Ignorable="d" d:DesignWidth="800" d:DesignHeight="450"
        x:Class="MyNamespace.MainWindow"
        Title="UpDock Testing"
		xmlns:up="clr-namespace:Avalonia.UpDock.Controls;assembly=Avalonia.UpDock">

    <up:SplitPanel Name="DockingHost" Fractions="1, 1" Orientation="Horizontal">
        <up:DockingTabControl>
            <TabItem Header="Tab A">
                <TextBlock Margin="5">Content</TextBlock>
            </TabItem>
        </up:DockingTabControl>
        <up:DockingTabControl>
            <TabItem Header="Tab B">
                <TextBlock Margin="5">More content</TextBlock>
            </TabItem>
            <TabItem Header="Tab C">
                <TextBlock Margin="5">Even more content</TextBlock>
            </TabItem>
        </up:DockingTabControl>
    </up:SplitPanel>
</Window>
```
MainWindow.axaml.cs
```cs
namespace MyNamespace;

public partial class MainWindow : Window
{
    private DockingManager? _dockingManager;

    public MainWindow()
    {
        InitializeComponent(true, true);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (Design.IsDesignMode)
            return;

        _dockingManager = new DockingManager(this, DockingHost);
    }
}
```
