# RimDiplomacy Sound Effects Implementation

## Overview
Implement vanilla-like communication sound effects for the `Dialog_DiplomacyDialogue` window to enhance immersion.

## Requirements
1.  **Open Sound**: Play `CommsWindow_Open` when the window opens.
2.  **Close Sound**: Play `CommsWindow_Close` when the window closes.
3.  **Ambience Sound**: Loop `RadioComms_Ambience` while the window is open.

## Implementation Details
### File: `RimDiplomacy/UI/Dialog_DiplomacyDialogue.cs`
-   Add `private Sustainer sustainer;` field to manage the looping sound.
-   In `Dialog_DiplomacyDialogue` constructor:
    -   Set `this.soundAppear = DefDatabase<SoundDef>.GetNamed("CommsWindow_Open");` to handle the opening sound automatically.
    -   Set `this.soundClose = DefDatabase<SoundDef>.GetNamed("CommsWindow_Close");` to handle the closing sound automatically.
-   Override `PostOpen()`:
    -   Call `base.PostOpen()`.
    -   Initialize and start `sustainer` using `DefDatabase<SoundDef>.GetNamed("RadioComms_Ambience").TrySpawnSustainer(SoundInfo.OnCamera(MaintenanceType.None))`.
-   Override `PreClose()` (modify existing):
    -   Stop `sustainer` if it exists (`sustainer.End()`).
    -   Set `sustainer = null`.
    -   Call `base.PreClose()`.
