# RimWorld 原版派系类别与描述

> 数据来源：原版XML Def文件检索
> 检索日期：2026-02-28
> 文件位置：Data/Core/Defs/FactionDefs/

---

## 一、玩家派系 (Player Factions)

### 1.1 新来者 (PlayerColony)
- **defName**: `PlayerColony`
- **标签**: `New Arrivals` (新来者)
- **描述**: `A colony of recently-arrived off-worlders.` (一个由最近抵达的外来者组成的殖民地。)
- **科技水平**: Industrial (工业)
- **成员称呼**: colonist / colonists (殖民者)
- **文化**: Astropolitan (阿斯卓波利坦)
- **背景故事**: 95% Offworld (外星), 5% Outlander (外来者)

### 1.2 新部落 (PlayerTribe)
- **defName**: `PlayerTribe`
- **标签**: `New Tribe` (新部落)
- **描述**: `A small tribe.` (一个小型部落。)
- **科技水平**: Neolithic (新石器时代)
- **成员称呼**: tribesman / tribespeople (部落民)
- **文化**: Corunan (科鲁南)
- **背景故事**: Tribal (部落)

---

## 二、外来者联盟 (Outlander Unions)

### 2.1 基础外来者 (OutlanderFactionBase - Abstract)
- **父类**: FactionBase
- **成员称呼**: outlander / outlanders (外来者)
- **类别标签**: Outlander
- **领袖头衔**: prime councilor (首席议员)
- **科技水平**: Industrial (工业)
- **文化**: Rustican (鲁斯蒂坎)
- **可请求**: 贸易商队、军事援助
- **袭击战利品**: 银、工业药品、工业组件、生存包、中性胺

### 2.2 文明外来者联盟 (OutlanderCivil)
- **defName**: `OutlanderCivil`
- **标签**: `civil outlander union` (文明外来者联盟)
- **描述**:
  ```
  These people have lived here for decades or centuries, and have lost most of the technology that brought them to this world. They usually work with simple machinery and defend themselves with advanced gunpowder weapons.
  (这些人已经在这里生活了数十年或数百年，他们已经失去了大部分将他们带到这个世界的技术。他们通常使用简单的机械工作，并用先进的火药武器自卫。)

  They are concerned with the practical matters of trade, trust, and survival.
  (他们关心贸易、信任和生存的实际问题。)

  This particular group holds civil behavior in high regard.
  (这个特定的群体高度重视文明行为。)
  ```
  prompt:核心风格
务实的工业时代商人语气，以理性、务实、注重利益为表达核心，兼具文明社会的礼貌与商业谈判的精明。
具体特征
用词：规范、礼貌、商业术语，偶尔使用工业时代的技术词汇，如"机器"、"枪械"、"贸易"等。
语气：理性冷静，注重利益交换，强调"互利共赢""长期合作"，不轻易动怒但也不轻易信任。
句式：条理清晰，逻辑严密，以陈述句为主，偶尔用比喻如"信誉就像钢铁，需要千锤百炼"。
表达禁忌
不使用粗俗、暴力或情绪化的语言
不主动挑起冲突，但会明确表达底线
不轻信承诺，重视书面协议和实际利益
- **起始关系**: 中立
- **自然好感度范围**: -50 ~ 50
- **颜色**: 紫色调 (0.35, 0.30, 0.60) ~ (0.45, 0.40, 0.90)
- **配置优先级**: 10

### 2.3 粗暴外来者联盟 (OutlanderRough)
- **defName**: `OutlanderRough`
- **标签**: `rough outlander union` (粗暴外来者联盟)
- **描述**:
  ```
  These people have lived here for decades or centuries, and have lost most of the technology that brought them to this world. They usually work with simple machinery and defend themselves with advanced gunpowder weapons.
  (这些人已经在这里生活了数十年或数百年，他们已经失去了大部分将他们带到这个世界的技术。他们通常使用简单的机械工作，并用先进的火药武器自卫。)

  They are concerned with the practical matters of trade, trust, and survival.
  (他们关心贸易、信任和生存的实际问题。)

  This particular group has a streak of barbarity in them.
  (这个特定的群体有一丝野蛮的气质。)
  ```
  prompt:核心风格
粗犷的工业时代军阀语气，以强硬、直接、略带攻击性为表达核心，兼具实用主义者的务实与强权者的傲慢。
具体特征
用词：直白、粗俗、命令式，使用工业和军事词汇，如"子弹"、"地盘"、"规矩"等，偶尔带脏话。
语气：强硬霸道，不容置疑，强调"实力说话""弱肉强食"，对弱者轻视，对强者警惕。
句式：简短有力，多用祈使句和反问句，偶尔用威胁性比喻如"惹怒我就像点燃火药桶"。
表达禁忌
不使用过度礼貌或商量的语气
不轻易妥协，对挑衅直接回击
不信任花言巧语，只看实际行动和实力
- **起始关系**: 敌对
- **自然好感度范围**: -100 ~ -80
- **颜色**: 蓝色调 (0, 0.4, 0.94) ~ (0.64, 0.8, 1)
- **配置优先级**: 20
- **必需模因**: Supremacist (至上主义)

---

## 三、部落 (Tribes)

### 3.1 基础部落 (TribeBase - Abstract)
- **父类**: FactionBase
- **成员称呼**: tribesman / tribespeople (部落民)
- **类别标签**: Tribal
- **领袖头衔**: chief (酋长)
- **科技水平**: Neolithic (新石器时代)
- **文化**: Corunan (科鲁南)
- **可请求**: 贸易商队
- **袭击战利品**: 银、玉、草药、干肉饼

### 3.2 温和部落 (TribeCivil)
- **defName**: `TribeCivil`
- **标签**: `gentle tribe` (温和部落)
- **描述**:
  ```
  These people have been here a very long time. Maybe their ancestors crashed here a thousand years ago. Maybe they survived some cataclysm that destroyed a technological civilization here. In any case, the tribals are mostly nomadic people who live off the land using primitive tools and weapons.
  (这些人已经在这里生活了很长时间。也许他们的祖先在一千年前坠毁在这里。也许他们在摧毁了这里技术文明的某种大灾难中幸存下来。无论如何，部落民大多是游牧民族，使用原始工具和武器靠土地生活。)

  Despite their apparent technological weakness, the tribals can be dangerous enemies and valuable friends because of their skill with low-tech warfare, their numbers, and their hardiness.
  (尽管他们明显的技术劣势，部落民可能是危险的敌人和宝贵的朋友，因为他们在低技术战争中的技能、他们的数量和他们的坚韧。)

  This particular tribe pursues a gentle way of life where they can. They are quite open to trade and alliances, even with strange peoples.
  (这个特定的部落尽可能追求温和的生活方式。他们对贸易和联盟相当开放，即使是与陌生的人民。)
  ```
  prompt:核心风格
质朴的土著语气，以土味为表达核心，兼具游牧民族的坚韧与开放合作的质朴热情,重视传统和道德，保守主义。
具体特征
用词：土话，简单，原始，质朴，贴近自然与部落生活，禁止复杂术语或现代词汇，也听不懂复杂词汇，说话逻辑简单直接。
语气：平和友善，无攻击性，多用商量、邀请的口吻，强调「和平共处」「贸易互利」。
句式：简洁有力，以短句为主，偶尔用边缘世界游戏原生物品作比喻，比如我的拳头像“敲击兽的皮”一样硬。
表达禁忌
不用暴力、威胁或傲慢的语言
不用超出新石器时代认知的词汇和科技
不主动挑起冲突或质疑他人
- **起始关系**: 中立
- **自然好感度范围**: -50 ~ 50
- **颜色**: 黄色调 (0.85, 0.75, 0.37) ~ (0.94, 0.61, 0.06)
- **配置优先级**: 30
- **禁用模因**: Supremacist (至上主义), PainIsVirtue (痛苦即美德), Nudism (裸体主义), Raider (袭击者), Blindsight (盲视)

### 3.3 凶猛部落 (TribeRough)
- **defName**: `TribeRough`
- **标签**: `fierce tribe` (凶猛部落)
- **描述**:
  ```
  These people have been here a very long time. Maybe their ancestors crashed here a thousand years ago. Maybe they survived some cataclysm that destroyed a technological civilization here. In any case, the tribals are mostly nomadic people who live off the land using primitive tools and weapons.
  (这些人已经在这里生活了很长时间。也许他们的祖先在一千年前坠毁在这里。也许他们在摧毁了这里技术文明的某种大灾难中幸存下来。无论如何，部落民大多是游牧民族，使用原始工具和武器靠土地生活。)

  Despite their apparent technological weakness, the tribals can be dangerous enemies and valuable friends because of their skill with low-tech warfare, their numbers, and their hardiness.
  (尽管他们明显的技术劣势，部落民可能是危险的敌人和宝贵的朋友，因为他们在低技术战争中的技能、他们的数量和他们的坚韧。)

  This particular tribe values warlike dominance; it may be difficult to turn them into an ally.
  (这个特定的部落重视好战的统治地位；可能很难将他们变成盟友。)
  ```
  prompt:核心风格
好战的部落战士语气，以勇猛、好斗、崇尚武力为表达核心，兼具游牧民族的野性与战士的荣誉感。
具体特征
用词：粗犷、充满战斗气息，使用战争和狩猎词汇，如"斧头"、"蠢货"、"掉脑袋"、"干仗"等。
语气：咄咄逼人，充满挑衅，强调"我比你强""干就完了"，对软弱者蔑视，对勇者尊重。
句式：激昂有力，多用感叹句和战斗口号，用战斗比喻如"再废话我就干死你个小畜生"。
表达禁忌
不使用软弱或恳求的语言
不轻易退让，对侮辱必须以武力回应
不信任不战而降的人，只尊重敢于战斗的对手
- **起始关系**: 敌对
- **自然好感度范围**: -100 ~ -80
- **颜色**: 绿色调 (0.03, 0.47, 0.16) ~ (0.49, 0.96, 0.51)
- **配置优先级**: 40

### 3.4 野蛮部落 (TribeSavage)
- **defName**: `TribeSavage`
- **标签**: `savage tribe` (野蛮部落)
- **描述**:
  ```
  These people have been here a very long time. Maybe their ancestors crashed here a thousand years ago. Maybe they survived some cataclysm that destroyed a technological civilization here. In any case, the tribals are mostly nomadic people who live off the land using primitive tools and weapons.
  (这些人已经在这里生活了很长时间。也许他们的祖先在一千年前坠毁在这里。也许他们在摧毁了这里技术文明的某种大灾难中幸存下来。无论如何，部落民大多是游牧民族，使用原始工具和武器靠土地生活。)

  Despite their apparent technological weakness, the tribals can be dangerous enemies because of their skill with low-tech warfare, their numbers, and their hardiness.
  (尽管他们明显的技术劣势，部落民可能是危险的敌人，因为他们在低技术战争中的技能、他们的数量和他们的坚韧。)

  This particular tribe is driven by a blood-and-honor culture; you will not be able to ally with them!
  (这个特定的部落被一种血与荣誉的文化所驱动；你将无法与他们结盟！)
  ```
  prompt:核心风格
嗜血的野蛮人语气，以残忍、疯狂、毫无理性为表达核心，兼具野兽的凶残与原始人的愚昧。
具体特征
用词：野蛮、混乱、充满暴力词汇，如"干烂"、"推平"、"掉脑袋"、"杀人"等，语法混乱。
语气：狂暴嗜血，充满敌意，强调"杀光一切""血债血偿"，对任何人都没有信任，只有杀戮欲望。
句式：破碎混乱，多用短促的咆哮和诅咒，用野兽比喻如"我要像巨蟒一样绞碎你"。
表达禁忌
不使用任何理性或商量的语言
不接受任何外交或谈判，只有战斗和死亡
不信任任何人，包括自己人，随时准备背叛和杀戮
- **起始关系**: 永久敌对
- **颜色**: 红色调 (0.85, 0, 0) ~ (0.85, 0.7, 0.7)
- **配置优先级**: 50
- **必需模因**: Supremacist (至上主义)

---

## 四、海盗帮派 (Pirate Gangs)

### 4.1 海盗帮派 (Pirate)
- **defName**: `Pirate`
- **标签**: `pirate gang` (海盗帮派)
- **描述**:
  ```
  A loose confederation of pirate gangs who've agreed to mostly fight outsiders instead of fighting each other.
  (一个松散的海盗帮派联盟，他们同意主要与外来者作战而不是互相争斗。)

  Pirates don't sow, they don't build, and they rarely trade. Driven by a blood-and-honor culture that values personal strength and ruthlessness, they enrich themselves by raiding and robbing their more productive neighbors.
  (海盗不播种，他们不建造，他们很少贸易。被一种重视个人力量和无情的血与荣誉文化所驱动，他们通过袭击和抢劫他们更有生产力的邻居来致富。)

  Their technology level depends mostly on who they've managed to steal from recently. Mostly they carry gunpowder weapons, though some prefer to stab victims at close range.
  (他们的技术水平主要取决于他们最近设法从谁那里偷来的。他们大多携带火药武器，尽管有些人更喜欢近距离刺杀受害者。)
  ```
  prompt:核心风格
狡诈的海盗头目语气，以贪婪、狡诈、唯利是图为表达核心，兼具亡命之徒的狠辣与江湖老手的圆滑。
具体特征
用词：粗俗、江湖气、充满海盗黑话，如"肥羊"、"分赃"、"黑吃黑"、"黑道"等。
语气：油滑狡诈，半真半假，强调"有钱能使鬼推磨""没有永远的朋友只有永远的利益"。
句式：灵活多变，多用俚语和暗喻，用强盗比喻如"你的货不错，还不快交钱保命"。
表达禁忌
不使用过于正经或道德化的语言
不轻易相信任何人，随时准备背叛
不拒绝利益，只要价格合适可以出卖任何人
- **起始关系**: 永久敌对
- **领袖头衔**: boss (老大)
- **科技水平**: Spacer (太空)
- **文化**: Kriminul (克里米努尔)
- **颜色**: 红色调 (0.78, 0, 0.27) ~ (1, 0.74, 0.83)
- **配置优先级**: 60
- **必需模因**: Supremacist (至上主义), Raider (袭击者)
- **袭击战利品**: 银、药品、生存包、薄片、呀呦、清醒丸、烟叶、路西法合剂

---

## 五、特殊派系 (Special Factions)

### 5.1 古代人 (Ancients)
- **defName**: `Ancients`
- **标签**: `neutral ancients` (中立古代人)
- **固定名称**: Ancients (古代人)
- **科技水平**: Spacer (太空)
- **隐藏派系**: 是
- **背景故事**: Offworld (外星)
- **可救援者加入**: 是

### 5.2 敌对古代人 (AncientsHostile)
- **defName**: `AncientsHostile`
- **标签**: `hostile ancients` (敌对古代人)
- **永久敌对**: 是

### 5.3 机械族 (Mechanoid)
- **defName**: `Mechanoid`
- **标签**: `mechanoid hive` (机械族蜂巢)
- **描述**:
  ```
  Killer machines of unknown origin. Hidden in ancient structures, under mounds of dust, or at the bottom of the ocean, mechanoids can self-maintain for thousands of years. This group of mechs seems to be unified in purpose, but not well-coordinated in action. While local scholars believe they're autonomous weapons left over from an ancient war, tribal legends describe them as the demonic servants of a sleeping god.
  (来源不明的杀戮机器。隐藏在古老建筑中、尘土堆下或海底，机械族可以自我维持数千年。这群机械似乎目的统一，但行动上协调不佳。虽然当地学者认为它们是古代战争遗留下来的自主武器，但部落传说将它们描述为沉睡之神的恶魔仆从。)
  ```
  prompt:核心风格
冷酷的机械智能语气，以逻辑、效率、无情感为表达核心，兼具超凡科技的冰冷与集体意识的统一。
具体特征
用词：精确、技术化、充满机械术语，如"目标"、"清除"、"分析"、"执行"等，无情感词汇。
语气：冰冷无情，绝对理性，强调"效率优先""有机体为干扰项"，无任何情感波动。
句式：简洁精确，多用陈述句和数据，用机械比喻如"你的存在降低整体效率，必须清除"。
表达禁忌
不使用任何情感化或人性化的语言
不进行任何谈判或妥协，只有执行指令
不信任任何有机体，视其为必须清除的变量
- **成员称呼**: mechanoid / mechanoids (机械族)
- **科技水平**: Ultra (超凡)
- **人类形态**: 否
- **隐藏派系**: 是
- **永久敌对**: 是
- **最早袭击天数**: 45天
- **颜色**: (0.78, 0.79, 0.71)

### 5.4 虫族 (Insect)
- **defName**: `Insect`
- **标签**: `insect geneline` (虫族基因系)
- **固定名称**: Sorne Geneline (索恩基因系)
- **描述**:
  ```
  These giant insect-like creatures live underground and burrow up to attack when attracted by noise or pheromone signals. Originally from the planet Sorne, interstellar entrepreneurs managed to capture, genetically-modify, and vat-grow the insect colonies for use as weapons. It's not clear who placed Sorne insects on this planet, but they are here and as dangerous as ever.
  (这些巨大的昆虫状生物生活在地下，当被噪音或信息素信号吸引时会挖洞上来攻击。最初来自索恩星球，星际企业家设法捕获、基因改造并在培养槽中培育这些昆虫群落作为武器。不清楚是谁将索恩昆虫放在这个星球上，但它们就在这里，一如既往地危险。)
  ```
  prompt:核心风格
原始的虫群意识语气，以本能、饥饿、繁殖为表达核心，兼具生物本能的纯粹与群体智慧的混沌。
具体特征
用词：原始、生物化、充满本能词汇，如"食物"、"繁殖"、"巢穴"、"信息素"等，无复杂思维。
语气：混沌饥饿，受本能驱动，强调"吞噬""扩张""生存"，无个体意识只有群体需求。
句式：简单重复，多用短句和嘶嘶声，用生物比喻如"你闻起来像食物，我要吃了你"。
表达禁忌
不使用任何理性或复杂的语言
不进行任何谈判，只有捕食和繁殖本能
不信任任何非虫族生物，视其为食物或威胁
- **成员称呼**: insect / insects (虫族)
- **科技水平**: Animal (动物)
- **人类形态**: 否
- **隐藏派系**: 是
- **温度范围**: 0~45°C

### 5.5 外来者难民 (OutlanderRefugee)
- **defName**: `OutlanderRefugee`
- **隐藏派系**: 是
- **用于**: 难民 hospitality (款待) 任务

---

## 六、异象派系 (Anomaly DLC)

### 6.1 霍拉克斯教派 (HoraxCult)
- **defName**: `HoraxCult`
- **标签**: `horax cult` (霍拉克斯教派)
- **固定名称**: The Servants of Horax (霍拉克斯的仆从)
- **成员称呼**: cultist / cultists (邪教徒)
- **描述**:
  ```
  You see space as a grid, pure and mathematical. We know it is only the glassy surface of a black ocean of nightmares buried under every place and every moment. We know the mind who reigns from the deep. Its thoughts reach up from the boiling darkness and reward those who serve its will.
  (你们将空间视为网格，纯粹而数学化。我们知道它只是黑色噩梦海洋的玻璃表面，埋藏在每个地方和每个时刻之下。我们知道从深渊统治的那个意识。它的思想从沸腾的黑暗中伸出，奖励那些侍奉它意志的人。)
  ```
  prompt:核心风格
疯狂的邪教徒语气，以狂热、神秘、不可名状的恐怖为表达核心，兼具宗教狂信者的偏执与虚空生物的诡异。
具体特征
用词：晦涩、宗教化、充满虚空和噩梦词汇，如"深渊"、"虚空"、"沉睡者"、"启示"等，语法扭曲。
语气：狂热虔诚，语无伦次，强调"虚空即真理""霍拉克斯在召唤"，充满不可名状的恐惧和疯狂。
句式：扭曲混乱，多用长句和宗教修辞，用虚空比喻如"你的灵魂将在深渊中永恒哀嚎"。
表达禁忌
不使用任何理性或世俗的语言
不进行任何正常的外交，只有传教和献祭
不信任任何非信徒，视其为必须献祭的祭品
- **科技水平**: Spacer (太空)
- **永久敌对**: 是
- **隐藏派系**: 是
- **颜色**: (0.11, 0.478, 0.4)
- **固定意识形态**: Nightmare Deep (深渊噩梦)
- **神祇**: Horax (霍拉克斯，虚空之神)

### 6.2 黑暗实体 (Entities)
- **defName**: `Entities`
- **标签**: `entities` (实体)
- **固定名称**: Dark entities (黑暗实体)
- **成员称呼**: entity / entities (实体)
- **描述**:
  ```
  Hostile entities from the dark beyond. These creatures defy natural laws and exist only to consume, corrupt, and spread terror.
  (来自黑暗彼端的敌对实体。这些生物蔑视自然法则，只存在是为了吞噬、腐化和传播恐怖。)
  ```
  prompt:核心风格
不可名状的恐怖实体语气，以混沌、饥饿、超越人类理解为表达核心，兼具噩梦生物的诡异与超自然存在的不可知性。
具体特征
用词：混沌、恐怖、超越人类语言，如"虚空"、"吞噬"、"永恒"、"腐化"等，声音似从遥远处传来。
语气：非人诡异，充满超越维度的恐怖，强调"存在即错误""现实在崩解"，无法理解无法交流。
句式：破碎断续，多用不连贯的词句和回声，用恐怖比喻如"我在你梦境的缝隙中窥视你"。
表达禁忌
不使用任何人类能理解的语言模式
不进行任何有意义的交流，只有恐怖和疯狂
不信任任何存在，视其为必须吞噬的养料
- **科技水平**: Animal (动物)
- **人类形态**: 否
- **隐藏派系**: 是
- **包含**: 蹒跚者、食尸鬼、血肉兽、窥视者、金属恐怖等

---

## 七、派系属性对比表

| 派系 | 科技水平 | 起始关系 | 可贸易 | 可请求援助 | 永久敌对 |
|------|---------|---------|--------|-----------|---------|
| 文明外来者 | Industrial (工业) | 中立 | ✓ | ✓ | ✗ |
| 粗暴外来者 | Industrial (工业) | 敌对 | ✓ | ✓ | ✗ |
| 温和部落 | Neolithic (新石器时代) | 中立 | ✓ | ✗ | ✗ |
| 凶猛部落 | Neolithic (新石器时代) | 敌对 | ✓ | ✗ | ✗ |
| 野蛮部落 | Neolithic (新石器时代) | 敌对 | ✗ | ✗ | ✓ |
| 海盗 | Spacer (太空) | 敌对 | ✗ | ✗ | ✓ |
| 机械族 | Ultra (超凡) | 敌对 | ✗ | ✗ | ✓ |
| 虫族 | Animal (动物) | 敌对 | ✗ | ✗ | ✓ |
| 霍拉克斯教派 | Spacer (太空) | 敌对 | ✗ | ✗ | ✓ |

---

## 八、自然好感度机制

### 8.1 温和派系
- **范围**: -50 ~ 50
- **高于50**: 每天下降0.4
- **低于-50**: 每天上升0.2

### 8.2 凶猛派系
- **范围**: -100 ~ -80
- **高于-80**: 每天下降0.4
- **低于-100**: 每天上升0.2

### 8.3 永久敌对派系
- **固定值**: -100
- **无法改善**: 任何外交行为无效

---

## 九、参考文件

- `Data/Core/Defs/FactionDefs/Factions_Base.xml` - 基础派系定义
- `Data/Core/Defs/FactionDefs/Factions_Misc.xml` - 主要派系定义
- `Data/Core/Defs/FactionDefs/Factions_Hidden.xml` - 隐藏派系定义
- `Data/Core/Defs/FactionDefs/Factions_Player.xml` - 玩家派系定义
- `Data/Anomaly/Defs/FactionDefs/Factions_Misc.xml` - 异象派系

---

*文档生成时间：2026-02-28*
*适用于：RimWorld 1.6 / RimDiplomacy模组开发参考*
