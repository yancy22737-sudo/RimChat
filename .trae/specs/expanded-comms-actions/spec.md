# Expanded Comms Actions Specification

## Overview
Expand the `Dialog_DiplomacyDialogue` interface to include specific actions for Royalty DLC and Quests, presented as cards or collapsible panels.

## Requirements

### 1. UI Layout
-   **Location**: Below the Orbital Trader Card (if present), above the Faction List/Chat Area.
-   **Style**: Collapsible panel or card style.
-   **Content**:
    -   **Royalty DLC Card**: Visible if Royalty is active, faction is Empire, and negotiator has title.
    -   **Quest Card**: Visible if there are active quests involving this faction.

### 2. Royalty DLC Features
-   **Condition**:
    -   `ModsConfig.RoyaltyActive` is true.
    -   Target faction is the Empire (`FactionDefOf.Empire`).
    -   Negotiator (`pawn`) has a royal title (`pawn.royalty != null`).
-   **Content**:
    -   Title: "Royal Actions".
    -   Buttons/List:
        -   **Permits**: List available permits that can be used via comms (e.g., Call Aid, Call Laborers).
        -   *Implementation Note*: Since permits often require targeting or specific UI, we might provide a shortcut to the negotiator's permit tab or simulate the specific comms options if possible.
        -   *Simplified Approach*: Display a button "Open Permit Actions" that might open a sub-menu or redirect to the standard permit UI if applicable. However, comms-specific permits (like calling aid) usually appear in the float menu.
        -   **Refined Plan**: Scan `pawn.royalty.AllFactionPermits` for `RoyalTitlePermitDef` that are usable. If they are usable via comms, show a button. Clicking it triggers `permit.Worker.Use(...)`.

### 3. Quests & Special Events
-   **Condition**:
    -   Active quests (`QuestState.Ongoing`) where `Quest.InvolvedFactions` contains the current faction.
-   **Content**:
    -   Title: "Active Quests".
    -   List: Display active quests related to this faction.
    -   Action: Clicking a quest opens the Main Quest Tab with that quest selected (`MainTabWindow_Quests.Show(quest)`).

### 4. Biotech DLC
-   **Skipped**: As per user instruction, mechanics like summoning bosses are handled by the vanilla Float Menu and do not need to be in this dialog.

## Technical Details

### `Dialog_DiplomacyDialogue.cs`
-   Add `DrawExpandedActions(Rect rect)` method.
-   Add `DrawRoyaltyActions(Rect rect)`:
    -   Iterate `negotiator.royalty.AllFactionPermits`.
    -   Check `permit.Worker.CanUse(...)`.
    -   Button triggers usage.
-   Add `DrawQuestActions(Rect rect)`:
    -   Iterate `Find.QuestManager.QuestsListForReading`.
    -   Filter by `faction` and `State == Ongoing`.
    -   Button opens quest tab.

### `CommsConsolePatch.cs`
-   No changes needed if we are just adding UI to the dialog. The entry point remains the same.

## Corner Cases
-   **No Negotiator**: Royalty actions require a negotiator. If `negotiator` is null (debug open), hide these actions or disable them.
-   **Layout Overflow**: If too many cards appear, ensure the chat area shrinks or the cards are collapsible.

## Language Keys
-   `RimChat_RoyaltyActions`
-   `RimChat_QuestActions`
-   `RimChat_OpenQuest`
-   `RimChat_UsePermit`
