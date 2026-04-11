using System.Xml;
using Verse;

namespace RimChat.Util
{
    /// <summary>
    /// Helpers for safely removing legacy XML reference nodes from old saves
    /// without registering loadIDs in CrossRefHandler.
    ///
    /// Problem: Scribe_References.Look registers loadIDs in CrossRefHandler during
    /// LoadingVars. If the referenced object (Faction/Pawn) no longer exists,
    /// ResolvingReferences fails with "Not all loadIDs which were read were consumed".
    ///
    /// Solution: Directly remove the legacy XML nodes from curXmlParent during
    /// LoadingVars, so Scribe never sees them and CrossRefHandler is never involved.
    /// </summary>
    public static class LegacyScribeHelper
    {
        /// <summary>
        /// Remove a legacy reference XML node from the current Scribe parent.
        /// Call this during LoadingVars to consume old reference nodes
        /// (e.g. &lt;faction&gt;, &lt;speakerPawn&gt;, &lt;pawn&gt;)
        /// without registering them in CrossRefHandler.
        /// </summary>
        /// <param name="nodeName">The XML element name to remove (e.g. "faction", "speakerPawn").</param>
        public static void RemoveLegacyReferenceNode(string nodeName)
        {
            if (Scribe.mode != LoadSaveMode.LoadingVars)
            {
                return;
            }

            XmlNode curParent = Scribe.loader?.curXmlParent;
            if (curParent == null)
            {
                return;
            }

            XmlNode legacyNode = curParent[nodeName];
            if (legacyNode != null)
            {
                curParent.RemoveChild(legacyNode);
            }
        }

        /// <summary>
        /// Remove multiple legacy reference XML nodes from the current Scribe parent.
        /// </summary>
        /// <param name="nodeNames">The XML element names to remove.</param>
        public static void RemoveLegacyReferenceNodes(params string[] nodeNames)
        {
            if (Scribe.mode != LoadSaveMode.LoadingVars || nodeNames == null)
            {
                return;
            }

            XmlNode curParent = Scribe.loader?.curXmlParent;
            if (curParent == null)
            {
                return;
            }

            foreach (string nodeName in nodeNames)
            {
                XmlNode legacyNode = curParent[nodeName];
                if (legacyNode != null)
                {
                    curParent.RemoveChild(legacyNode);
                }
            }
        }
    }
}
