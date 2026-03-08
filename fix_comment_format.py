#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Script to fix comment formatting issues after translation
"""

import os
import re
import sys
from pathlib import Path

def fix_file(file_path):
    """Fix comment formatting in a single file"""
    print(f"Fixing: {file_path}")
    
    try:
        with open(file_path, 'r', encoding='utf-8') as f:
            content = f.read()
        
        original_content = content
        
        # Fix: // / <summary>... pattern
        content = re.sub(r'// /\s*<summary>(.*?)</summary>', r'/// <summary>\1</summary>', content, flags=re.DOTALL)
        
        # Fix: // / <summary> with newline issues
        content = re.sub(r'// /\s*<summary>(.*?)\s*// /</summary>', r'/// <summary>\1/// </summary>', content, flags=re.DOTALL)
        
        # Fix: // / on separate lines
        content = re.sub(r'^(\s*)// /\s*<summary>(.*?)\s*^(\s*)// /</summary>', r'\1/// <summary>\2\3/// </summary>', content, flags=re.MULTILINE | re.DOTALL)
        
        # Fix: // / at the beginning of lines
        content = re.sub(r'^(\s*)// /', r'\1///', content, flags=re.MULTILINE)
        
        # Only write if content changed
        if content != original_content:
            with open(file_path, 'w', encoding='utf-8') as f:
                f.write(content)
            
            print(f"  Fixed formatting")
            return True
        else:
            print(f"  No formatting issues found")
            return False
    
    except Exception as e:
        print(f"  Error: {e}")
        return False

def fix_directory(directory_path):
    """Fix comment formatting in all C# files in a directory"""
    directory = Path(directory_path)
    
    if not directory.exists():
        print(f"Directory not found: {directory_path}")
        return
    
    print(f"Fixing comment formatting in directory: {directory_path}")
    
    # Get all C# files
    cs_files = list(directory.rglob("*.cs"))
    
    print(f"Found {len(cs_files)} C# files")
    
    fixed_count = 0
    for file_path in cs_files:
        if fix_file(str(file_path)):
            fixed_count += 1
    
    print(f"\nFormatting fix completed! Fixed {fixed_count} out of {len(cs_files)} files.")

def main():
    if len(sys.argv) != 2:
        print("Usage: python fix_comment_format.py <directory>")
        print("Example: python fix_comment_format.py RimChat")
        sys.exit(1)
    
    target_dir = sys.argv[1]
    fix_directory(target_dir)

if __name__ == "__main__":
    main()