using System;

namespace RimChat.Prompting
{
    /// <summary>
    /// Dependencies: none (pure data model).
    /// Responsibility: flat prompt template entry, inspired by RimTalk PromptEntry.
    /// </summary>
    public enum PromptEntryRole
    {
        System = 0,
        User = 1,
        Assistant = 2
    }

    public enum PromptEntryPosition
    {
        Relative = 0,
        InChat = 1
    }

    public sealed class PromptTemplateEntry
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public string Content = string.Empty;
        public PromptEntryRole Role = PromptEntryRole.System;
        public string CustomRole = string.Empty;
        public PromptEntryPosition Position = PromptEntryPosition.Relative;
        public int InChatDepth;
        public bool Enabled = true;
        public bool IsMainChatHistory;
        public string SourceModId;
        public string Channel = string.Empty;
        public int Order;

        public PromptTemplateEntry Clone()
        {
            return new PromptTemplateEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = Name,
                Content = Content,
                Role = Role,
                CustomRole = CustomRole,
                Position = Position,
                InChatDepth = InChatDepth,
                Enabled = Enabled,
                IsMainChatHistory = IsMainChatHistory,
                SourceModId = SourceModId,
                Channel = Channel,
                Order = Order
            };
        }
    }
}
