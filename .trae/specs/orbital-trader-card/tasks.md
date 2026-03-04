# Tasks

- [ ] Modify `Dialog_DiplomacyDialogue.cs`
    - [ ] Add `negotiator` field and update constructor.
    - [ ] Implement `GetTradeShip()` helper method.
    - [ ] Implement `DrawOrbitalTraderCard(Rect rect)` method.
    - [ ] Update `DoWindowContents` to layout the card and adjust other elements.
    - [ ] Add logic to open `Dialog_Trade`.
- [ ] Modify `CommsConsolePatch.cs`
    - [ ] Update `CommsConsoleCallback` to pass `Pawn` to `Dialog_DiplomacyDialogue` constructor.
    - [ ] Update any other references to the constructor.
- [ ] Verify Implementation
    - [ ] Build the project to check for compilation errors.
    - [ ] Verify logic correctness (especially null checks).
