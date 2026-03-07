# 寤惰繜AI浜嬩欢瑙﹀彂瑙勬牸

## Why
褰撳墠AI鍔ㄤ綔鍦ㄥ璇濆悗绔嬪嵆鎵ц锛岀己涔忕湡瀹炲浜ょ殑鏃堕棿寤惰繜鎰熴€傞€氳繃娣诲姞娓告垙鍐呮椂闂村欢杩燂紝璁╃帺瀹舵湁鏇村绛栫暐绌洪棿锛屽寮烘父鎴忔矇娴告劅銆?
## What Changes
- 鏂板寤惰繜浜嬩欢绠＄悊鍣紝瀛樺偍鍜岀鐞嗗緟瑙﹀彂鐨凙I浜嬩欢
- 淇敼 `request_caravan` 鍜?`request_aid` 涓や釜鍔ㄤ綔锛屼娇鍏跺欢杩熻Е鍙?- 鍦ㄨ缃腑娣诲姞寤惰繜鏃堕棿閰嶇疆閫夐」锛堥粯璁?2灏忔椂锛?- 娣诲姞寤惰繜鏃堕棿璁＄畻閫昏緫锛氬熀纭€鏃堕棿 + 濂芥劅搴﹀奖鍝?卤50%) + 闅忔満鍋忕Щ(卤5灏忔椂)
- 娣诲姞寰呭鐞嗕簨浠舵煡鐪嬬晫闈紙鍙锛?- 闆嗘垚鍒版父鎴忓瓨妗ｇ郴缁?
## Impact
- Affected specs: 澶栦氦瀵硅瘽绯荤粺銆丄I鍔ㄤ綔鎵ц鍣?- Affected code: AIActionExecutor.cs, GameComponent_DiplomacyManager.cs, RimChatSettings.cs, Dialog_DiplomacyDialogue.cs

## ADDED Requirements

### Requirement: 寤惰繜浜嬩欢绠＄悊
绯荤粺 SHALL 鎻愪緵寤惰繜浜嬩欢鐨勫瓨鍌ㄣ€佽拷韪拰瑙﹀彂鏈哄埗銆?
#### Scenario: AI杩斿洖鍟嗛槦/鎻村姪璇锋眰
- **WHEN** AI鍦ㄥ璇濅腑杩斿洖 `request_caravan` 鎴?`request_aid` 鍔ㄤ綔
- **THEN** 鍔ㄤ綔涓嶇珛鍗虫墽琛岋紝鑰屾槸娣诲姞鍒板欢杩熶簨浠堕槦鍒?- **AND** 鐜╁鍙互鍦ㄥ緟澶勭悊浜嬩欢鍒楄〃涓煡鐪?
### Requirement: 鍔ㄦ€佸欢杩熻绠?绯荤粺 SHALL 鏍规嵁濂芥劅搴﹀拰闅忔満鍥犵礌璁＄畻寤惰繜鏃堕棿銆?
#### Scenario: 璁＄畻寤惰繜鏃堕棿
- **WHEN** 娣诲姞寤惰繜浜嬩欢鏃?- **THEN** 寤惰繜鏃堕棿 = 鍩虹鏃堕棿(12灏忔椂) 脳 (1 卤 濂芥劅搴﹀奖鍝嶇郴鏁? 卤 闅忔満鍋忕Щ(卤5灏忔椂)
- **AND** 濂芥劅搴﹁秺楂橈紝寤惰繜瓒婄煭锛涘ソ鎰熷害瓒婁綆锛屽欢杩熻秺闀?
### Requirement: 璁剧疆鍙厤缃欢杩熸椂闂?绯荤粺 SHALL 鍦∕od璁剧疆涓彁渚涘欢杩熸椂闂撮厤缃€夐」銆?
#### Scenario: 璋冩暣榛樿寤惰繜
- **WHEN** 鐜╁鎵撳紑Mod璁剧疆
- **THEN** 鍙互璋冩暣鍩虹寤惰繜鏃堕棿锛堝彲璋冩暣鑼冨洿寰呭畾锛?
### Requirement: 寰呭鐞嗕簨浠舵煡鐪?绯荤粺 SHALL 鎻愪緵寰呭鐞嗕簨浠剁殑鍙鏌ョ湅鐣岄潰銆?
#### Scenario: 鏌ョ湅寰呭鐞嗕簨浠?- **WHEN** 鐜╁鎵撳紑澶栦氦瀵硅瘽鐣岄潰
- **THEN** 鍙互鐪嬪埌璇ユ淳绯绘墍鏈夊緟澶勭悊鐨勫欢杩熶簨浠?
## REMOVED Requirements
鏃?