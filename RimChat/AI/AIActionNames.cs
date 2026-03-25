namespace RimChat.AI
{
    /// <summary>/// Dependencies: none.
 /// Responsibility: centralized AI action type identifiers shared across parser/executor/UI.
 ///</summary>
    public static class AIActionNames
    {
        public const string AdjustGoodwill = "adjust_goodwill";
        public const string SendGift = "send_gift";
        public const string RequestAid = "request_aid";
        public const string DeclareWar = "declare_war";
        public const string MakePeace = "make_peace";
        public const string RequestCaravan = "request_caravan";
        public const string RequestRaid = "request_raid";
        public const string RequestItemAirdrop = "request_item_airdrop";
        public const string RequestInfo = "request_info";
        public const string PayPrisonerRansom = "pay_prisoner_ransom";
        public const string RejectRequest = "reject_request";
        public const string TriggerIncident = "trigger_incident";
        public const string CreateQuest = "create_quest";
        public const string SendImage = "send_image";
        public const string PublishPublicPost = "publish_public_post";
        public const string ExitDialogue = "exit_dialogue";
        public const string GoOffline = "go_offline";
        public const string SetDnd = "set_dnd";
    }
}
