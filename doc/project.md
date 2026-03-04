Building_CommsConsole 有以下方法：

1. `get_CanUseCommsNow()`
2. `SpawnSetup(Map, Boolean)`
3. `GetCommTargets(Pawn)` - 获取通讯目标！
4. `GetFloatMenuOptions(Pawn)` - 这就是我们要拦截的方法！
5. `GiveUseCommsJob(Pawn, ICommunicable)` - 给予使用通讯的 Job