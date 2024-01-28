# What's UpDock?
nothing much...

*...except the best Docking library you'll ever use or alteast it will be.*

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
- Close Tabs `TODO`
- Drag Tab out of TabControl to create a new floating window
- Drag Tab into another TabControl, that's part of the same docking host (DockingManager), to add it as a tab
- Drag Tab into another TabControl to create a new split and Dock it there `TODO`

## Getting started
To use the DockingManager you need to have a docking host control that fits the following requirements:
1. The host itself is a SplitPanel
2. All children should be either a SplitPanel or a DockingTabControl (you are still allowed to use other controls, they just wont support docking)
3. All children that are SplitPanels must have the opposite `Orientation` of their Parent and follow the requirements **2** and **3**

### An Example

MainWindow.axaml
```cs

```
