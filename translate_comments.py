#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Script to translate Chinese comments to English in C# files
"""

import os
import re
import sys
from pathlib import Path

# Translation dictionary
TRANSLATION_DICT = {
    # Common terms
    "提示词": "prompt",
    "配置": "configuration",
    "派系": "faction",
    "全局": "global",
    "特定": "specific",
    "存储": "store",
    "用于": "used for",
    "显示": "display",
    "名称": "name",
    "系统": "system",
    "对话": "dialogue",
    "模板": "template",
    "启用": "enable",
    "是否": "whether",
    "ID": "ID",
    "为空": "empty",
    "获取": "get",
    "设置": "settings",
    "文件夹": "folder",
    "路径": "path",
    "初始化": "initialize",
    "管理器": "manager",
    "应用": "apply",
    "补丁": "patch",
    "和谐": "harmony",
    "动态": "dynamic",
    "方法": "method",
    "查找": "lookup",
    "状态": "state",
    "结果": "result",
    "成功": "success",
    "响应": "response",
    "错误": "error",
    "进度": "progress",
    "开始时间": "start time",
    "持续时间": "duration",
    "请求": "request",
    "处理": "processing",
    "完成": "completed",
    "空闲": "idle",
    "等待": "pending",
    "处理中": "processing",
    "已完成": "completed",
    "错误": "error",
    "通道": "channel",
    "未知": "unknown",
    "外交": "diplomacy",
    "角色扮演": "RPG",
    "模组": "mod",
    "成功": "successfully",
    "日志": "log",
    "消息": "message",
    "类别": "category",
    "翻译": "translate",
    "窗口": "window",
    "内容": "contents",
    "矩形": "rectangle",
    
    # UI related
    "界面": "interface",
    "用户": "user",
    "按钮": "button",
    "标签": "label",
    "文本": "text",
    "输入": "input",
    "输出": "output",
    "选择": "select",
    "保存": "save",
    "加载": "load",
    "编辑": "edit",
    "编辑器": "editor",
    "文件": "file",
    
    # Game specific
    "殖民地": "colony",
    "世界": "world",
    "事件": "event",
    "记录": "record",
    "领袖": "leader",
    "记忆": "memory",
    "会话": "session",
    "存在": "presence",
    "状态": "state",
    "关系": "relation",
    "好感度": "goodwill",
    "成本": "cost",
    "计算器": "calculator",
    "上下文": "context",
    "值": "values",
    "行为": "behavior",
    "阈值": "threshold",
    "基于": "based on",
    "规则": "rules",
    
    # AI specific
    "人工智能": "AI",
    "聊天": "chat",
    "服务": "service",
    "异步": "async",
    "提供者": "provider",
    "解析器": "parser",
    "执行器": "executor",
    "客户端": "client",
    "上下文": "context",
    "压缩": "compression",
    "服务": "service",
    "驱动": "driver",
    "工作": "job",
    "关系": "relation",
    "响应": "response",
    "应用程序接口": "API",
    
    # Technical terms
    "组件": "component",
    "兼容": "compatibility",
    "桥接": "bridge",
    "反射": "reflection",
    "模型": "model",
    "条目": "entry",
    "预设": "preset",
    "本地": "local",
    "模型": "model",
    "推送": "push",
    "频率": "frequency",
    "模式": "mode",
    "环境": "environment",
    "社交": "social",
    "圈子": "circle",
}

def translate_text(text):
    """Translate Chinese text to English"""
    if not text:
        return text
    
    result = text
    
    # Replace known terms
    for chinese, english in TRANSLATION_DICT.items():
        result = result.replace(chinese, english)
    
    # Clean up punctuation
    punctuation_map = {
        "。": ". ",
        "，": ", ",
        "：": ": ",
        "；": "; ",
        "？": "? ",
        "！": "! ",
        "、": ", ",
        "（": " (",
        "）": ") ",
        "《": '"',
        "》": '"',
        "【": "[",
        "】": "]",
        "—": "-",
    }
    
    for chinese_punct, english_punct in punctuation_map.items():
        result = result.replace(chinese_punct, english_punct)
    
    # Fix double spaces
    while "  " in result:
        result = result.replace("  ", " ")
    
    # Capitalize first letter if it's a sentence
    if result and result[0].islower():
        result = result[0].upper() + result[1:]
    
    return result.strip()

def process_file(file_path):
    """Process a single C# file"""
    print(f"Processing: {file_path}")
    
    try:
        with open(file_path, 'r', encoding='utf-8') as f:
            content = f.read()
        
        original_content = content
        
        # Process XML documentation comments (/// <summary>...</summary>)
        def replace_xml_comment(match):
            inner_text = match.group(1).strip()
            translated = translate_text(inner_text)
            return f"/// <summary>{translated}</summary>"
        
        content = re.sub(r'///\s*<summary>([^<]+)</summary>', replace_xml_comment, content, flags=re.MULTILINE)
        
        # Process single line comments (//)
        def replace_single_line_comment(match):
            comment = match.group(1).strip()
            translated = translate_text(comment)
            return f"// {translated}"
        
        content = re.sub(r'//\s*([^\r\n]+)', replace_single_line_comment, content, flags=re.MULTILINE)
        
        # Process multi-line comments (/* */) - single line
        def replace_multi_line_comment(match):
            comment = match.group(1).strip()
            translated = translate_text(comment)
            return f"/* {translated} */"
        
        content = re.sub(r'/\*\s*([^*]+)\s*\*/', replace_multi_line_comment, content, flags=re.MULTILINE)
        
        # Only write if content changed
        if content != original_content:
            # Create backup
            backup_path = f"{file_path}.backup"
            with open(backup_path, 'w', encoding='utf-8') as f:
                f.write(original_content)
            
            # Write translated content
            with open(file_path, 'w', encoding='utf-8') as f:
                f.write(content)
            
            print(f"  Updated and backed up to {backup_path}")
            return True
        else:
            print(f"  No changes needed")
            return False
    
    except Exception as e:
        print(f"  Error: {e}")
        return False

def process_directory(directory_path):
    """Process all C# files in a directory"""
    directory = Path(directory_path)
    
    if not directory.exists():
        print(f"Directory not found: {directory_path}")
        return
    
    print(f"Starting comment translation for directory: {directory_path}")
    
    # Get all C# files
    cs_files = list(directory.rglob("*.cs"))
    
    print(f"Found {len(cs_files)} C# files")
    
    changed_count = 0
    for file_path in cs_files:
        if process_file(str(file_path)):
            changed_count += 1
    
    print(f"\nTranslation completed! Changed {changed_count} out of {len(cs_files)} files.")

def main():
    if len(sys.argv) != 2:
        print("Usage: python translate_comments.py <directory>")
        print("Example: python translate_comments.py RimChat")
        sys.exit(1)
    
    target_dir = sys.argv[1]
    process_directory(target_dir)

if __name__ == "__main__":
    main()