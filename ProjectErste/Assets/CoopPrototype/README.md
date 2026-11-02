# Shape Co-op prototype

- Open `Assets/CoopPrototype/Scenes/ShapeCoop.unity` in Unity 6000.3.27f1, start Steam and sign in, then press Play. The scene is also the first scene in Build Settings.
- For Windows builds, Steamworks.NET supplies the native plugin and the build postprocessor writes `steam_appid.txt` beside the executable. Copy the entire build directory to each test PC.
- Host: Create room. Other players: Find rooms, then choose the host's room. Wait for each intended participant to show connected, then the host selects Start game. One, two, or three connected players are supported. A failed connection is shown and excluded from the game after timeout.
- A / D: world-left / world-right, Space: jump, Shift: dash in the most recent movement direction, F: freeze / unfreeze. Any player can request a restart; the host executes it. The clock continues across restarts and freezes on the first success.
- Select the `Shape Coop System` prefab or scene instance to adjust `Prototype Settings` in the Inspector. The world, primitive visuals, colliders and immediate-mode UI are created at runtime; `PrototypeVisuals` contains the replaceable presentation code, while `PrototypePlayer` contains movement and collision logic.
- Test with separate Steam accounts and machines. The identifier on the public App 480 lobby keeps searches limited to this prototype; it is not a private or secure room.
