# Tasks

- [ ] Modify `Dialog_DiplomacyDialogue.cs`
    - [ ] Add `DrawExpandedActions` method to `DoWindowContents`.
    - [ ] Implement `DrawRoyaltyActions`:
        - [ ] Check `ModsConfig.RoyaltyActive` and `faction.def`.
        - [ ] List usable permits for the negotiator.
    - [ ] Implement `DrawQuestActions`:
        - [ ] Find relevant quests.
        - [ ] Display buttons to open quest tab.
    - [ ] Add collapsible/expandable logic for these sections.
- [ ] Add Language Keys
    - [ ] Add keys for headers and buttons in English and Chinese.
- [ ] Verify
    - [ ] Check layout with Orbital Trader card + Royalty card + Quest card present simultaneously.
