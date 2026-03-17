using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RimChat.Prompting
{
    /// <summary>/// Dependencies: none.
 /// Responsibility: in-memory hierarchical prompt node model.
 ///</summary>
    public sealed class PromptHierarchyNode
    {
        public string Id;
        public string Content;
        public readonly List<PromptHierarchyNode> Children = new List<PromptHierarchyNode>();

        public PromptHierarchyNode(string id, string content = "")
        {
            Id = id ?? "section";
            Content = content ?? string.Empty;
        }

        public PromptHierarchyNode AddChild(string id, string content = "")
        {
            var child = new PromptHierarchyNode(id, content);
            Children.Add(child);
            return child;
        }
    }

    /// <summary>/// Dependencies: PromptHierarchyNode.
 /// Responsibility: render prompt tree as XML-like blocks or indented structured sections.
 ///</summary>
    public static class PromptHierarchyRenderer
    {
        public static string Render(PromptHierarchyNode root)
        {
            if (root == null)
            {
                return string.Empty;
            }

            return RenderAsXml(root);
        }

        public static string Render(PromptHierarchyNode root, bool useXmlTags)
        {
            return Render(root);
        }

        private static string RenderAsXml(PromptHierarchyNode root)
        {
            var sb = new StringBuilder();
            RenderXmlNode(sb, root, 0);
            return sb.ToString().Trim();
        }

        private static void RenderXmlNode(StringBuilder sb, PromptHierarchyNode node, int depth)
        {
            if (node == null)
            {
                return;
            }

            string indent = new string(' ', depth * 2);
            string tag = NormalizeTag(node.Id);
            sb.Append(indent).Append('<').Append(tag).Append('>').AppendLine();

            if (!string.IsNullOrWhiteSpace(node.Content))
            {
                string contentIndent = new string(' ', (depth + 1) * 2);
                string escaped = EscapeXml(node.Content.Trim());
                string[] lines = escaped.Replace("\r", string.Empty).Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    sb.Append(contentIndent).AppendLine(lines[i]);
                }
            }

            for (int i = 0; i < node.Children.Count; i++)
            {
                RenderXmlNode(sb, node.Children[i], depth + 1);
            }

            sb.Append(indent).Append("</").Append(tag).Append('>').AppendLine();
        }
        private static string NormalizeTag(string raw)
        {
            string value = (raw ?? "section").Trim().ToLowerInvariant();
            if (value.Length == 0)
            {
                return "section";
            }

            var sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-')
                {
                    sb.Append(c);
                }
                else if (c == ' ' || c == '/' || c == ':')
                {
                    sb.Append('_');
                }
            }

            string tag = sb.ToString().Trim('_');
            return string.IsNullOrEmpty(tag) ? "section" : tag;
        }

        private static string EscapeXml(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }
    }
}
