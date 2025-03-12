# What's UpDock?
nothing much...

*...except the best Docking library you'll ever use.*


**Look and feel is heavily inspired by the [docking branch of Dear ImGUI](https://github.com/ocornut/imgui/tree/docking)**
![Avalonia.UpDock Testing_Screenshot](https://github.com/user-attachments/assets/c0318c4f-f5ca-4d3d-910f-925d386e150d)


## Why?
Because the two other Dock libraries that exist either have on outdated style or just don't work reliably.  
This library aims to work with any style, theme and whatnot by simply using or slightly extending
the functionality of existing controls and hooking into a few UI events and ItemTemplates.  
This should in theory also make this library future-proof.

## Public Controls

- `DockSpacePanel` hosts the regular dock slots and acts as a root of the dock tree
- `SplitPanel` hosts the split dock slots (they resize based on percentage)
- `RearrangeTabControl` holds the (docked) tabs (allows for rearranging and dragging out tabs)
- `ClosableTabItem` a drop in replacement for `TabItem`s to make the closable

## Features
- Rearrange Tabs
- Close Tabs
- Drag Tab out of TabControl to create a new floating window
- Drag Tab into another `TabControl`, that's a descendant of the same `DockSpacePanel`, to add it as a tab
- Drag Tab into another Control to create a new split and "Dock" it there
- Drag Tab into another Control to truly Dock it there and push the other control(s) aside
- Drag Tab to the border of the docking host

## Getting started
Add a `DockSpacePanel`, Design a Layout using `SplitPanel`s (make sure to set Fractions and Orientation) and fill the slots with Children of type `SplitPanel` or `DockingTabControl`.  
*Technically you can use any `Control` but only `RearrangeTabControl` supports dragging tabs out*

### An Example

ExampleWindow.axaml
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
                <up:ClosableTabItem Header="Tab C[losable]">
                    <TextBlock Margin="5">Even more content</TextBlock>
                </up:ClosableTabItem>
            </up:RearrangeTabControl>
        </up:SplitPanel>
    </up:DockSpacePanel>
</Window>
```
Should look something like this:
![Avalonia UpDock_Example_Screenshot](https://github.com/user-attachments/assets/b1e78e02-c9fb-4236-8588-496e508bdd8c)
