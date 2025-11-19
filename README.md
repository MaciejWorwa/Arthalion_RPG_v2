# Arthalion RPG

Description

# Setup

You can download the game from the Releases section. Both Windows and Linux versions are available.
Execute the StandaloneWindows64.exe or StandaloneLinux64.exe executable files, located in the downloaded .zip file, and enjoy!

# Environment

The game was developed in Unity Engine. List of supported OS:

- [x] Windows
- [x] Linux

# Language Pack

At the moment only Polish language is supported.

# Features

- Battlefield editor with hundreds of map assets from https://www.forgotten-adventures.net/. Author retains all copyright rights,

- Various races and monsters to choose from,

- Various weapons with quality selection,

- A wide variety of spells and all Arcanes to choose from,

- 10 different game modes (including automatic combat mode),

- Save and load system,

- Covering map areas,

- Customizable backgrounds,

- Interactive unit management,

- Unit size considerations,

- Inventory system allowing weapon and armor editing, weight calculation, and money management,

- Implementation of relevant talents and skills affecting combat,

- And more...

# Tutorial

## Map Editor

When you start the game, you enter the **Map Editor**. Here, you can customize the battlefield using the following features:

- **Grid Size**: Adjust the grid size to fit your gameplay needs.
- **Background Settings**: Load a custom background and modify its size and position to align with your grid.
- **Map Elements**: Place various elements on the map and adjust their rotation:
  - **Element Rotation**: Right-click before placing an element on the map to rotate it by 90 degrees. You can also set random rotation. When enabled, each placed element will have a random rotation.
  - **Element Types**:
    - **High Obstacle**: Fully covers units behind it.
    - **Low Obstacle**: Partially covers units behind it.
  - **Blocking Tiles**: Set elements to block the tile they occupy. The last item in the element list is specifically designed for blocking; check the box to enable this, making it invisible in Battle Mode.
- When your battlefield setup is complete, click **Play** in the top right corner to start the battle.

## Camera Controls

- **Panning**: Hold the middle mouse button and drag to pan the camera. Alternatively, you can hold **Alt** and use the arrow keys on your keyboard.
- **Zooming**: Use the scroll wheel to zoom in and out for a closer or broader view.

## Starting Battle Mode

- **Click "Play"**: When your battlefield setup is complete, click **Play** in the top right corner to start the battle.
- **Game Modes and Settings**: Press **Esc** to open the main menu, where you can access **Settings**. Here, you can adjust the game modes; by default, recommended settings are enabled.

## Battle Mode

In Battle Mode, manage and control units with these actions:

- **Adding Units:** Open the Unit Management Panel, click on the name of a unit, and then click on the desired location on the grid to place it. Alternatively, use the button to add a unit at a random position on the battlefield.
- **Selecting Units:** Left-click a unit to select it. To select multiple units, enable the appropriate mode in the Unit Management Panel and drag the mouse over the desired units. You can copy selected units with `Ctrl + C` and paste them using `Ctrl + V`.
- **Moving Units:** Left-click on an empty tile within the unit's movement range to move the selected unit. Movement range is visually highlighted.
- **Attacking:** Right-click on an enemy unit to initiate an attack. Hold the mouse button to choose a specific hit location.
- **Editing Units:** After selecting a unit, you can edit its attributes in the panel located on the left side of the screen.
- **Deleting Units:** Press `Delete` to remove selected units or use the "Delete" button in the Unit Management Panel.
- **Drag and Drop Units or Map Elements**: Move elements freely across the map using drag and drop. Press `Delete` to remove the selected element or right-click to rotate it.

## Covering Areas

This feature is accessible from the main menu. It allows you to hide or reveal parts of the map. To use it, activate the appropriate button, then either click on specific tiles or drag the mouse across the grid. You can toggle this mode on or off by pressing **Ctrl+W**.

Additionally you can use the **right mouse button (RMB)** to number covered tiles. The functionality works as follows:

- **RMB**: Increment the number on the selected tile, cycling from `0` to `9`. When the number reaches `9`, pressing **RMB** again will disable numbering on that tile.
- **Ctrl + RMB**: Immediately disable numbering on the selected tile, regardless of its current number.

This numbering system is helpful for organizing or annotating hidden areas of the map.

## Game Modes and Settings

Press **Esc** to open the main menu, where you can access **Settings**. By default, the settings are optimized for manual gameplay during RPG sessions. For testing purposes, it is recommended to enable the automatic modes.

1. **Automatic Combat** (Ctrl+A): Actions for all units are automated, preventing manual movement when enabled.
2. **Automatic Death** (Ctrl+K): Units with health below zero are automatically removed.
3. **Automatic Unit Selection** (Ctrl+Q): Units are selected in initiative order.
4. **Automatic Defense** (Ctrl+D): Units automatically decide to parry or dodge attacks. When disabled, players choose to block, dodge, or take the damage.
5. **Automatic Dice Rolls** (Ctrl+R): Disable to allow players to use physical dice; manual outcomes can then be entered.
6. **Include Fear Mechanics** (Ctrl+T): When enabled, the mechanics of Fear are applied.
7. **Friendly Fire** (Ctrl+F): Enables attacking allied units.
8. **Enemy Stats Hiding Mode** (Ctrl+I): Hides the statistics panel of enemy units when they are selected.
9. **Unit Name Hiding Mode** (Ctrl+N): Hides the names of units on their tokens.
10. **Health Points Hiding Mode** (Ctrl+H): Hides the health points of units on their tokens.

## Hotkeys

Here are the available hotkeys to streamline gameplay:

- **Ctrl + S**: Save the current game state.
- **Ctrl + W**: Toggle **Covering Areas** mode.
- **Ctrl + M**: Toggle map element panel in **Battle Mode**.
- **Ctrl + C**: Copy selected units.
- **Ctrl + V**: Paste copied units.
- **Delete**: Remove selected units.
- **I**: Open the inventory.
