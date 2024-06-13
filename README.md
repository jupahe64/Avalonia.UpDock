# What's UpDock?
nothing much...

*...except the best Docking library you'll ever use or alteast it will be.*

![Avalonia UpDock Testing_Screenshot](https://github.com/jupahe64/Avalonia.UpDock/assets/33004544/30118643-158c-4c6e-bb1f-5409aa363873)



## Why?
Because the two other Dock libraries that exists either have on outdated style or just don't work reliably.  
This library aims to work with any style, theme and whatnot by simply using or slightly extending the functionality of existing controls and hocking into a few UI events and ItemTemplates.  
This should in theory also make this library future proof.

## Public Controls

- `DockingHost` to host the layout
- `SplitPanel` to host the docking slots (allows for resizing)
- `DockingTabControl` to hold the (docked) tabs (allows for rearranging)

## Features
- Rearrange Tabs
- Close Tabs
- Drag Tab out of TabControl to create a new floating window
- Drag Tab into another TabControl, that's part of the same docking host (DockingManager), to add it as a tab
- Drag Tab into another TabControl to create a new split and Dock it there

## Getting started
Add DockingHost, Design a Layout using SplitPanels (make sure to set Fractions) and fill the slots with Children of type DockingTabControl.

### An Example

MainWindow.axaml
```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        mc:Ignorable="d" d:DesignWidth="800" d:DesignHeight="450"
        x:Class="CODE_BEHIND_CLASS_GOES_HERE"
        Title="Example"
        xmlns:up="clr-namespace:Avalonia.UpDock.Controls;assembly=Avalonia.UpDock">

    <up:DockingHost>
        <up:SplitPanel Fractions="1, 1" Orientation="Horizontal">
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
    </up:DockingHost>
</Window>
```
Should look something like this:
![Avalonia UpDock_Example_Screenshot](https://github.com/jupahe64/Avalonia.UpDock/assets/33004544/c5a7acf8-10d6-4c21-8048-4cf96ccd9a7b)
