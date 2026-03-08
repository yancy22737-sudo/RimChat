using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace RimChat.Tools
{
    public class CommentTranslator
    {
        private static readonly Dictionary<string, string> TranslationDictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Common terms
            { "提示词", "prompt" },
            { "配置", "configuration" },
            { "派系", "faction" },
            { "全局", "global" },
            { "特定", "specific" },
            { "存储", "store" },
            { "用于", "used for" },
            { "显示", "display" },
            { "名称", "name" },
            { "系统", "system" },
            { "对话", "dialogue" },
            { "模板", "template" },
            { "启用", "enable" },
            { "是否", "whether" },
            { "ID", "ID" },
            { "为空", "empty" },
            { "获取", "get" },
            { "设置", "settings" },
            { "文件夹", "folder" },
            { "路径", "path" },
            { "初始化", "initialize" },
            { "管理器", "manager" },
            { "应用", "apply" },
            { "补丁", "patch" },
            { "和谐", "harmony" },
            { "动态", "dynamic" },
            { "方法", "method" },
            { "查找", "lookup" },
            { "状态", "state" },
            { "结果", "result" },
            { "成功", "success" },
            { "响应", "response" },
            { "错误", "error" },
            { "进度", "progress" },
            { "开始时间", "start time" },
            { "持续时间", "duration" },
            { "请求", "request" },
            { "处理", "processing" },
            { "完成", "completed" },
            { "空闲", "idle" },
            { "等待", "pending" },
            { "处理中", "processing" },
            { "已完成", "completed" },
            { "错误", "error" },
            { "通道", "channel" },
            { "未知", "unknown" },
            { "外交", "diplomacy" },
            { "角色扮演", "RPG" },
            { "模组", "mod" },
            { "成功", "successfully" },
            { "日志", "log" },
            { "消息", "message" },
            { "类别", "category" },
            { "翻译", "translate" },
            { "窗口", "window" },
            { "内容", "contents" },
            { "矩形", "rectangle" },
            
            // UI related
            { "界面", "interface" },
            { "用户", "user" },
            { "按钮", "button" },
            { "标签", "label" },
            { "文本", "text" },
            { "输入", "input" },
            { "输出", "output" },
            { "选择", "select" },
            { "保存", "save" },
            { "加载", "load" },
            { "编辑", "edit" },
            { "编辑器", "editor" },
            { "文件", "file" },
            
            // Game specific
            { "殖民地", "colony" },
            { "世界", "world" },
            { "事件", "event" },
            { "记录", "record" },
            { "领袖", "leader" },
            { "记忆", "memory" },
            { "会话", "session" },
            { "存在", "presence" },
            { "状态", "state" },
            { "关系", "relation" },
            { "好感度", "goodwill" },
            { "成本", "cost" },
            { "计算器", "calculator" },
            { "上下文", "context" },
            { "值", "values" },
            { "行为", "behavior" },
            { "阈值", "threshold" },
            { "基于", "based on" },
            { "规则", "rules" },
            
            // AI specific
            { "人工智能", "AI" },
            { "聊天", "chat" },
            { "服务", "service" },
            { "异步", "async" },
            { "提供者", "provider" },
            { "解析器", "parser" },
            { "执行器", "executor" },
            { "客户端", "client" },
            { "上下文", "context" },
            { "压缩", "compression" },
            { "服务", "service" },
            { "驱动", "driver" },
            { "工作", "job" },
            { "关系", "relation" },
            { "响应", "response" },
            { "应用程序接口", "API" },
            
            // Technical terms
            { "组件", "component" },
            { "兼容", "compatibility" },
            { "桥接", "bridge" },
            { "反射", "reflection" },
            { "模型", "model" },
            { "条目", "entry" },
            { "预设", "preset" },
            { "本地", "local" },
            { "模型", "model" },
            { "推送", "push" },
            { "频率", "frequency" },
            { "模式", "mode" },
            { "环境", "environment" },
            { "社交", "social" },
            { "圈子", "circle" },
            { "核心", "core" },
            { "外交系统", "diplomacy system" },
            { "社交", "social" },
            { "帖子", "post" },
            { "行动", "action" },
            { "意图", "intent" },
            { "解析器", "resolver" },
            { "服务", "service" },
            { "状态", "state" },
            { "枚举", "enum" },
            { "资格", "eligibility" },
            { "服务", "service" },
            { "延迟", "delayed" },
            { "事件", "event" },
            { "管理器", "manager" },
            { "通知", "notification" },
            { "管理器", "manager" },
            { "接口", "interface" },
            { "游戏", "game" },
            { "节点", "node" },
            { "注入", "inject" },
            { "石板", "slate" },
            { "语法", "grammar" },
            { "部分", "part" },
            { "回调", "callback" },
            { "内存", "memory" },
            { "跨通道", "cross-channel" },
            { "摘要", "summary" },
            { "记录", "record" },
            { "服务", "service" },
            { "对话", "dialogue" },
            { "总结", "summary" },
            { "服务", "service" },
            { "会话", "session" },
            { "领袖", "leader" },
            { "记忆", "memory" },
            { "存在", "presence" },
            { "状态", "state" },
            { "JSON", "JSON" },
            { "编解码器", "codec" },
            { "管理器", "manager" },
            { "跟踪", "track" },
            { "追踪器", "tracker" },
            { "存档", "archive" },
            { "管理器", "manager" },
            { "NPC", "NPC" },
            { "对话", "dialogue" },
            { "推送", "push" },
            { "管理器", "manager" },
            { "模型", "model" },
            { "补丁", "patch" },
            { "通讯", "comms" },
            { "控制台", "console" },
            { "退出", "exit" },
            { "地图", "map" },
            { "交互", "interaction" },
            { "杀死", "kill" },
            { "交易", "trade" },
            { "交易", "deal" },
            { "用户界面", "UI" },
            { "根", "root" },
            { "播放", "play" },
            { "角色扮演", "RPG" },
            { "推送", "push" },
            { "候选人", "candidate" },
            { "生成", "generation" },
            { "模型", "model" },
            { "持久化", "persistence" },
            { "场景", "scenario" },
            { "上下文", "context" },
            { "提示", "prompt" },
            { "持久化", "persistence" },
            { "服务", "service" },
            { "文件", "file" },
            { "管理器", "manager" },
            { "分层", "hierarchical" },
            { "殖民地", "colony" },
            { "上下文", "context" },
            { "模板", "template" },
            { "变量", "variable" },
            { "模型", "model" },
            { "提示", "prompt" },
            { "层次结构", "hierarchy" },
            { "文本", "text" },
            { "构建器", "builder" },
            { "关系", "relation" },
            { "好感度", "goodwill" },
            { "成本", "cost" },
            { "关系", "relation" },
            { "上下文", "context" },
            { "值", "values" },
            { "角色扮演", "RPG" },
            { "关系", "relation" },
            { "值", "values" },
            { "基于", "based on" },
            { "计算器", "calculator" },
            { "规则", "rules" },
            { "行为", "behavior" },
            { "阈值", "threshold" },
            { "用户界面", "UI" },
            { "对话框", "dialog" },
            { "外交", "diplomacy" },
            { "对话", "dialogue" },
            { "行动", "action" },
            { "提示", "hint" },
            { "存在", "presence" },
            { "社交", "social" },
            { "圈子", "circle" },
            { "视图", "view" },
            { "策略", "strategy" },
            { "派系", "faction" },
            { "提示", "prompt" },
            { "编辑器", "editor" },
            { "加载", "load" },
            { "文件", "file" },
            { "提示", "prompt" },
            { "变量", "variable" },
            { "选择器", "picker" },
            { "角色扮演", "RPG" },
            { "角色", "pawn" },
            { "对话", "dialogue" },
            { "行动", "action" },
            { "提示", "hint" },
            { "行动", "action" },
            { "保存", "save" },
            { "文件", "file" },
            { "选择", "select" },
            { "派系", "faction" },
            { "对话", "dialogue" },
            { "增强", "enhanced" },
            { "文本区域", "text area" },
            { "五维", "five-dimension" },
            { "条", "bar" },
            { "好感度", "goodwill" },
            { "变化", "change" },
            { "动画", "animation" },
            { "主标签", "main tab" },
            { "窗口", "window" },
            { "社交", "social" },
            { "圈子", "circle" },
            { "实用工具", "utility" },
            { "DLC", "DLC" },
            { "兼容性", "compatibility" },
            { "调试", "debug" },
            { "记录器", "logger" },
            { "世界状态", "world state" },
            { "事件", "event" },
            { "账本", "ledger" },
            { "组件", "component" },
            { "记录", "records" }
        };

        public static string TranslateComment(string comment)
        {
            if (string.IsNullOrEmpty(comment))
                return comment;

            string result = comment;
            
            // Replace known terms
            foreach (var kvp in TranslationDictionary)
            {
                result = result.Replace(kvp.Key, kvp.Value);
            }

            // Clean up common patterns
            result = result.Replace(" - ", " - ");
            result = result.Replace("（", " (");
            result = result.Replace("）", ") ");
            result = result.Replace("《", "\"");
            result = result.Replace("》", "\"");
            result = result.Replace("【", "[");
            result = result.Replace("】", "]");
            result = result.Replace("。", ". ");
            result = result.Replace("，", ", ");
            result = result.Replace("；", "; ");
            result = result.Replace("：", ": ");
            result = result.Replace("？", "? ");
            result = result.Replace("！", "! ");
            result = result.Replace("、", ", ");

            // Fix double spaces
            while (result.Contains("  "))
                result = result.Replace("  ", " ");

            // Capitalize first letter if it's a sentence
            if (result.Length > 0 && char.IsLower(result[0]))
            {
                result = char.ToUpper(result[0]) + result.Substring(1);
            }

            return result.Trim();
        }

        public static string ProcessFile(string filePath)
        {
            if (!File.Exists(filePath))
                return "File not found";

            try
            {
                string content = File.ReadAllText(filePath, Encoding.UTF8);
                string processedContent = ProcessContent(content);
                
                // Backup original file
                string backupPath = filePath + ".backup";
                File.Copy(filePath, backupPath, true);
                
                // Write processed content
                File.WriteAllText(filePath, processedContent, Encoding.UTF8);
                
                return $"Processed: {filePath} (backup saved to {backupPath})";
            }
            catch (Exception ex)
            {
                return $"Error processing {filePath}: {ex.Message}";
            }
        }

        private static string ProcessContent(string content)
        {
            // Process XML documentation comments (///)
            content = Regex.Replace(content, @"///\s*<summary>([^<]+)</summary>", match =>
            {
                string innerText = match.Groups[1].Value.Trim();
                string translated = TranslateComment(innerText);
                return $"/// <summary>{translated}</summary>";
            }, RegexOptions.Multiline);

            // Process single line comments (//)
            content = Regex.Replace(content, @"//\s*([^\r\n]+)", match =>
            {
                string comment = match.Groups[1].Value.Trim();
                string translated = TranslateComment(comment);
                return $"// {translated}";
            }, RegexOptions.Multiline);

            // Process multi-line comments (/* */) - single line
            content = Regex.Replace(content, @"/\*\s*([^*]+)\s*\*/", match =>
            {
                string comment = match.Groups[1].Value.Trim();
                string translated = TranslateComment(comment);
                return $"/* {translated} */";
            }, RegexOptions.Multiline);

            return content;
        }

        public static void ProcessDirectory(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                Console.WriteLine($"Directory not found: {directoryPath}");
                return;
            }

            // Get all C# files
            string[] csFiles = Directory.GetFiles(directoryPath, "*.cs", SearchOption.AllDirectories);
            
            Console.WriteLine($"Found {csFiles.Length} C# files to process");
            
            foreach (string file in csFiles)
            {
                Console.WriteLine(ProcessFile(file));
            }
            
            Console.WriteLine("Processing completed!");
        }

        public static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: CommentTranslator.exe <directory_path>");
                Console.WriteLine("Example: CommentTranslator.exe C:\\MyProject");
                return;
            }

            string path = args[0];
            
            if (File.Exists(path))
            {
                Console.WriteLine(ProcessFile(path));
            }
            else if (Directory.Exists(path))
            {
                ProcessDirectory(path);
            }
            else
            {
                Console.WriteLine($"Path not found: {path}");
            }
        }
    }
}