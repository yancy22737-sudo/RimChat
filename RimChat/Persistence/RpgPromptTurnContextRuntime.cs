using System;

namespace RimChat.Persistence
{
    /// <summary>
    /// Dependencies: RPG prompt assembly call sites.
    /// Responsibility: carry transient per-request RPG prompt hints without persisting them.
    /// </summary>
    public sealed class RpgPromptTurnContextRuntime
    {
        public string CurrentTurnUserIntent = string.Empty;
        public bool AllowMemoryCompressionScheduling = true;
        public bool AllowMemoryColdLoad = true;
    }

    /// <summary>
    /// Dependencies: single-threaded prompt build flow.
    /// Responsibility: scope transient RPG prompt runtime context for one prompt build.
    /// </summary>
    public sealed class RpgPromptTurnContextScope : IDisposable
    {
        [ThreadStatic]
        private static RpgPromptTurnContextRuntime current;

        private readonly RpgPromptTurnContextRuntime previous;
        private bool disposed;

        private RpgPromptTurnContextScope(RpgPromptTurnContextRuntime previousContext)
        {
            previous = previousContext;
        }

        public static RpgPromptTurnContextRuntime Current => current;

        public static IDisposable Push(
            string currentTurnUserIntent,
            bool allowMemoryCompressionScheduling = true,
            bool allowMemoryColdLoad = true)
        {
            RpgPromptTurnContextRuntime previousContext = current;
            current = new RpgPromptTurnContextRuntime
            {
                CurrentTurnUserIntent = currentTurnUserIntent?.Trim() ?? string.Empty,
                AllowMemoryCompressionScheduling = allowMemoryCompressionScheduling,
                AllowMemoryColdLoad = allowMemoryColdLoad
            };

            return new RpgPromptTurnContextScope(previousContext);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            current = previous;
            disposed = true;
        }
    }
}
