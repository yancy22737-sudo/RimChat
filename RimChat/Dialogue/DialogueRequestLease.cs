using System;

namespace RimChat.Dialogue
{
    /// <summary>
    /// Tracks request ownership and lifecycle for a single dialogue window.
    /// </summary>
    public sealed class DialogueRequestLease
    {
        private readonly object syncRoot = new object();
        private string requestId = string.Empty;

        public string DialogueSessionId { get; }
        public string OwnerWindowId { get; }
        public int ContextVersion { get; }
        public bool IsClosing { get; private set; }
        public bool IsDisposed { get; private set; }

        public DialogueRequestLease(string dialogueSessionId, string ownerWindowId, int contextVersion)
        {
            DialogueSessionId = dialogueSessionId ?? string.Empty;
            OwnerWindowId = ownerWindowId ?? string.Empty;
            ContextVersion = contextVersion;
        }

        public string RequestId
        {
            get
            {
                lock (syncRoot)
                {
                    return requestId;
                }
            }
        }

        public void BindRequestId(string newRequestId)
        {
            lock (syncRoot)
            {
                if (IsDisposed || IsClosing)
                {
                    return;
                }

                requestId = newRequestId ?? string.Empty;
            }
        }

        public void MarkClosing()
        {
            lock (syncRoot)
            {
                IsClosing = true;
            }
        }

        public void Dispose()
        {
            lock (syncRoot)
            {
                IsClosing = true;
                IsDisposed = true;
            }
        }

        public bool IsValidFor(string callbackRequestId, string callbackDialogueSessionId, int callbackContextVersion)
        {
            lock (syncRoot)
            {
                if (IsDisposed || IsClosing)
                {
                    return false;
                }

                if (!string.Equals(requestId, callbackRequestId ?? string.Empty, StringComparison.Ordinal))
                {
                    return false;
                }

                if (!string.Equals(DialogueSessionId, callbackDialogueSessionId ?? string.Empty, StringComparison.Ordinal))
                {
                    return false;
                }

                return ContextVersion == callbackContextVersion;
            }
        }
    }
}
