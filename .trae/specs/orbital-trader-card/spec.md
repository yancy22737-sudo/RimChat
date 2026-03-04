# Orbital Trader Card Specification

## Overview
Implement a pinned card component at the top of the `Dialog_DiplomacyDialogue` interface that appears when an orbital trader from the faction is present. Clicking the card opens the vanilla trade dialog.

## Requirements
1.  **Display Logic**:
    -   Check if an orbital trader (`TradeShip`) belonging to the current faction exists in the current map's `passingShipManager`.
    -   If found, display the card at the top of the dialog.
    -   The card must be fixed and not scroll with the chat or faction list.

2.  **UI Design**:
    -   **Position**: Below the title bar, above the faction list and chat area.
    -   **Height**: Approximately 60px.
    -   **Content**:
        -   Icon: Trade ship icon or generic trade icon.
        -   Text: "Orbital Trader in range" or similar.
        -   Button: "Trade" (or make the whole card clickable).
    -   **Style**: Standard card style consistent with existing UI.

3.  **Interaction**:
    -   Clicking the "Trade" button closes the current `Dialog_DiplomacyDialogue` and opens the vanilla `Dialog_Trade`.
    -   Requires a `Pawn negotiator` to be passed to `Dialog_Trade`.
    -   If no negotiator is available (e.g., debug open), the button should be disabled or show a tooltip.

4.  **Data Flow**:
    -   Modify `Dialog_DiplomacyDialogue` constructor to accept `Pawn negotiator`.
    -   Update `CommsConsolePatch` to pass the pawn interacting with the console.

## Technical Details

### `Dialog_DiplomacyDialogue.cs`
-   Add `private Pawn negotiator;` field.
-   Update constructor: `public Dialog_DiplomacyDialogue(Faction faction, Pawn negotiator = null)`.
-   In `DoWindowContents`:
    -   Call `DrawOrbitalTraderCard(Rect rect)` if a trade ship is found.
    -   Adjust `contentY` for subsequent elements (Faction List, Chat Area) to accommodate the card.
-   Implement `DrawOrbitalTraderCard`:
    -   Find trade ship: `Find.CurrentMap.passingShipManager.passingShips.FirstOrDefault(x => x.Faction == faction) as TradeShip`.
    -   Draw UI elements.
    -   Handle click: `Find.WindowStack.Add(new Dialog_Trade(negotiator, tradeShip)); Close();`.

### `CommsConsolePatch.cs`
-   Update `CommsConsoleCallback.RegisterCallback` to store the pawn.
-   Update `CommsConsoleCallback.MapComponentTick` to pass the pawn when creating `Dialog_DiplomacyDialogue`.

## Corner Cases
-   **No Negotiator**: If opened via debug or other means without a pawn, disable the trade button with a tooltip "No negotiator available".
-   **Trade Ship Departs**: If the ship leaves while dialog is open, the card should disappear (handled by checking `passingShips` every frame in `DoWindowContents`).
-   **Multiple Trade Ships**: If multiple ships from same faction (rare), pick the first one.

## Compatibility
-   Uses vanilla `Dialog_Trade` and `TradeShip` classes.
-   Ensures compatibility with existing `CommsConsolePatch`.
