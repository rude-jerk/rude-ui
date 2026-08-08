# RudeUI

A deliberately small Dalamud HUD plugin: ElvUI-like player and target frame layout, drawn with the restrained charcoal, ivory, and antique-gold language of FFXIV's native UI.

## Use

- `/rudeui` opens settings.
- `/rudeui lock` toggles frame movement.
- Unlock the frames and drag either one into place.
- The stock parameter and target widgets can be hidden independently.

RudeUI includes configurable player and target health frames, a compact target-of-target bar, independently movable player and target cast bars, interruptibility colors, and a slide-cast window. It does not replace party frames, job gauges, nameplates, or hotbars.

Build with `dotnet build -c Debug`, then install the generated plugin output through Dalamud's developer plugin locations.
