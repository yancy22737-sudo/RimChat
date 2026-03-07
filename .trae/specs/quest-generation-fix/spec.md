# Quest 鐢熸垚閿欒淇 Spec

## Why

AI 瀵硅瘽绯荤粺鍦ㄨ皟鐢?`CreateQuest` API 鏃讹紝澶氫釜鍘熺増浠诲姟妯℃澘鐢熸垚澶辫触锛屽鑷寸孩瀛楁姤閿欏拰浠诲姟鏃犳硶姝ｅ父鍒涘缓銆備富瑕侀棶棰樺寘鎷細
1. `OpportunitySite_ItemStash` 浠诲姟鍥?`sitePartsParams=null` 瀵艰嚧 NullReferenceException
2. `Mission_BanditCamp` 浠诲姟鍥?`requiredPawnCount=-1` 鏃犳晥鍙傛暟鎶ラ敊
3. Grammar 瑙ｆ瀽澶辫触锛堜腑鏂囩炕璇戠己澶卞叧閿瓧娈碉級

## What Changes

- **淇 `OpportunitySite_ItemStash` 浠诲姟鐢熸垚**锛氱‘淇?`sitePartsParams` 姝ｇ‘鍒濆鍖栵紝娣诲姞蹇呰鐨勬淳绯诲拰绔欑偣鍙傛暟
- **淇 `Mission_BanditCamp` 浠诲姟鐢熸垚**锛氱‘淇?`requiredPawnCount` 鍩轰簬鐜╁浜哄彛姝ｇ‘璁＄畻
- **澧炲己閿欒澶勭悊**锛氬綋鍘熺増浠诲姟鍙傛暟涓嶅畬鏁存椂锛岃嚜鍔ㄥ洖閫€鍒伴€氱敤浠诲姟 `RimChat_AIQuest`
- **鏀硅繘鍙傛暟楠岃瘉**锛氬湪浠诲姟鐢熸垚鍓嶉妫€鏌ュ叧閿弬鏁帮紝鎻愬墠鎷︽埅涓嶅悎鐞嗙殑浠诲姟璇锋眰

## Impact

- Affected specs: CreateQuest API, AIActionExecutor
- Affected code: 
  - `RimChat/DiplomacySystem/GameAIInterface.cs` (CreateQuest 鏂规硶)
  - `RimChat/AI/AIActionExecutor.cs` (ExecuteCreateQuest 鏂规硶)

## ADDED Requirements

### Requirement: Quest Parameter Validation

绯荤粺搴斿湪璋冪敤鍘熺増浠诲姟鐢熸垚鍓嶉獙璇佸叧閿弬鏁扮殑瀹屾暣鎬у拰鏈夋晥鎬с€?
#### Scenario: OpportunitySite_ItemStash 鍙傛暟楠岃瘉
- **WHEN** AI 璇锋眰鍒涘缓 `OpportunitySite_ItemStash` 浠诲姟
- **THEN** 绯荤粺搴旂‘淇濓細
  - `points` >= 200锛堟渶灏忓▉鑳佺偣鏁帮級
  - `siteFaction` 瀛樺湪涓旈潪姘镐箙鏁屽
  - 濡傛灉娌℃湁 `asker`锛岃缃?`askerIsNull=true`

#### Scenario: Mission_BanditCamp 鍙傛暟楠岃瘉
- **WHEN** AI 璇锋眰鍒涘缓 `Mission_BanditCamp` 浠诲姟
- **THEN** 绯荤粺搴旂‘淇濓細
  - `requiredPawnCount` 鍩轰簬鐜╁鑷敱娈栨皯鑰呮暟閲忚绠楋紙2-5浜猴級
  - `enemyFaction` 瀛樺湪涓斾负鏁屽娲剧郴
  - `enemiesLabel` 宸茶缃?
### Requirement: Graceful Fallback

褰撳師鐗堜换鍔＄敓鎴愬け璐ユ椂锛岀郴缁熷簲鑷姩鍥為€€鍒伴€氱敤浠诲姟妯℃澘銆?
#### Scenario: 绔欑偣鐢熸垚澶辫触鍥為€€
- **WHEN** 鍘熺増浠诲姟鐢熸垚鎶涘嚭 `NullReferenceException` 鎴栫珯鐐圭敓鎴愰敊璇?- **THEN** 绯荤粺搴旇嚜鍔ㄤ娇鐢?`RimChat_AIQuest` 妯℃澘鍒涘缓浠诲姟
- **AND** 璁板綍璀﹀憡鏃ュ織璇存槑鍥為€€鍘熷洜

#### Scenario: 娲剧郴涓嶅吋瀹瑰洖閫€
- **WHEN** 浠诲姟妯℃澘瑕佹眰鐗瑰畾娲剧郴绫诲瀷锛堝 Empire锛変絾褰撳墠娲剧郴涓嶅尮閰?- **THEN** 绯荤粺搴旇嚜鍔ㄥ洖閫€鍒板吋瀹圭殑浠诲姟妯℃澘

## MODIFIED Requirements

### Requirement: CreateQuest Method

鍘?`CreateQuest` 鏂规硶闇€瑕佸寮哄弬鏁伴澶勭悊閫昏緫锛?
**淇敼鍓?*锛氱洿鎺ヤ紶閫掑弬鏁板埌 QuestGen锛屼緷璧栧師鐗堣剼鏈鐞嗙己澶卞弬鏁?**淇敼鍚?*锛氬湪璋冪敤 QuestGen 鍓嶈嚜鍔ㄨˉ鍏ㄥ繀瑕佸弬鏁帮紝楠岃瘉鍙傛暟鏈夋晥鎬?
鍏抽敭淇敼鐐癸細
1. `OpportunitySite_ItemStash` 闇€瑕佺‘淇?`sitePartsParams` 涓嶄负 null
2. `Mission_BanditCamp` 闇€瑕佺‘淇?`requiredPawnCount` > 0
3. 鎵€鏈変换鍔￠渶瑕佺‘淇?`points` 鏈夊悎鐞嗛粯璁ゅ€?
## Root Cause Analysis

### 闂1: sitePartsParams = null

```
Slate vars:
sitePartsParams=null
```

**鍘熷洜**锛歚QuestNode_GetSitePartDefsByTagsAndFaction` 鑺傜偣鏈兘姝ｇ‘鐢熸垚绔欑偣閮ㄤ欢瀹氫箟锛屽鑷村悗缁?`QuestNode_GetDefaultSitePartsParams` 鏃犳硶鐢熸垚鍙傛暟銆?
**瑙ｅ喅鏂规**锛氬湪璋冪敤鍘熺増鑴氭湰鍓嶏紝棰勮缃?`siteFaction`锛岀‘淇濇淳绯讳笂涓嬫枃瀹屾暣銆?
### 闂2: requiredPawnCount = -1

```
Mission 'Quest4.BanditCamp' of type 'QuestNode_Root_Mission_BanditCamp' and def 'Mission_BanditCamp' has invalid required pawn count (-1) or population (3).
```

**鍘熷洜**锛歚Mission_BanditCamp` 鐨?`QuestNode_Root_Mission_BanditCamp` 鑺傜偣鍐呴儴璁＄畻 `requiredPawnCount` 鏃朵娇鐢ㄤ簡鐜╁浜哄彛锛屼絾浼犲叆鐨勫弬鏁拌鐩栦簡璁＄畻閫昏緫銆?
**瑙ｅ喅鏂规**锛氫笉浼犻€?`requiredPawnCount` 鍙傛暟锛岃鍘熺増鑴氭湰鑷璁＄畻锛涙垨纭繚浼犲叆鍊?>= 2銆?
### 闂3: Grammar 瑙ｆ瀽澶辫触

```
Grammar unresolvable. Root 'questDescription'
```

**鍘熷洜**锛氫腑鏂囩炕璇戠己灏?`allSitePartsDescriptionsExceptFirst` 绛夊叧閿瓧娈电殑缈昏瘧瑙勫垯銆?
**瑙ｅ喅鏂规**锛氱‘淇濈珯鐐归儴浠舵纭敓鎴愶紝杩欐牱 Grammar 绯荤粺鎵嶈兘鐢熸垚鎻忚堪鏂囨湰銆?
## Technical Notes

1. `QuestNode_GetSitePartDefsByTagsAndFaction` 闇€瑕佹湁鏁堢殑娲剧郴涓婁笅鏂囨墠鑳介€夋嫨鍚堥€傜殑绔欑偣閮ㄤ欢
2. `Mission_BanditCamp` 鏄?Royalty DLC 浠诲姟锛岄渶瑕佹鏌?DLC 鏄惁婵€娲?3. 姘镐箙鏁屽娲剧郴锛堝娴风洍锛変笉搴斿彂璧峰浜ょ被浠诲姟
