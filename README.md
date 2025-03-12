# What's UpDock?
nothing much...

*...except the best Docking library you'll ever use or alteast it will be.*

![Avalonia UpDock Testing_Screenshot](https://github.com/jupahe64/Avalonia.UpDock/assets/33004544/30118643-158c-4c6e-bb1f-5409aa363873)



## Why?
Because the two other Dock libraries that exists either have on outdated style or just don't work reliably.  
This library aims to work with any style, theme and whatnot by simply using or slightly extending the functionality of existing controls and hocking into a few UI events and ItemTemplates.  
This should in theory also make this library future proof.

## Public Controls

- `DockSpacePanel` to host the layout
- `SplitPanel` to host the docking slots (allows for resizing)
- `RearrangeTabControl` to hold the (docked) tabs (allows for rearranging)

## Features
- Rearrange Tabs
- Close Tabs
- Drag Tab out of TabControl to create a new floating window
- Drag Tab into another TabControl, that's a descendant of the same DockingHost, to add it as a tab
- Drag Tab into another Control '' to create a new split and "Dock" it there
- Drag Tab into another Control '' to truly Dock it there and push the other control(s) aside
- Drag Tab to the border of the docking host ''

## Getting started
Add a `DockSpacePanel`, Design a Layout using `SplitPanel`s (make sure to set Fractions and Orientation) and fill the slots with Children of type `SplitPanel` or `DockingTabControl`.  
*Technically you can use any `Control` but only `RearrangeTabControl` supports dragging tabs out*

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

    <up:DockSpacePanel>
        <up:SplitPanel Fractions="1, 1" Orientation="Horizontal">
            <up:RearrangeTabControl>
                <TabItem Header="Tab A">
                    <TextBlock Margin="5">Content</TextBlock>
                </TabItem>
            </up:RearrangeTabControl>
            <up:RearrangeTabControl>
                <TabItem Header="Tab B">
                    <TextBlock Margin="5">More content</TextBlock>
                </TabItem>
                <TabItem Header="Tab C">
                    <TextBlock Margin="5">Even more content</TextBlock>
                </TabItem>
            </up:RearrangeTabControl>
        </up:SplitPanel>
    </up:DockSpacePanel>
</Window>
```
Should look something like this:
![Avalonia UpDock_Example_Screenshot](https://github.com/jupahe64/Avalonia.UpDock/assets/33004544/c5a7acf8-10d6-4c21-8048-4cf96ccd9a7b)
