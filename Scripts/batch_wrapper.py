#!/usr/bin/env python3
"""
Simple batch wrapper.
Usage:
  python batch_wrapper.py --target <script_filename> --pilihan <path> --master <path> --output <path>

The wrapper will locate the target script in the same folder and execute it with appropriate arguments.
It streams stdout/stderr to console so the host process can read it.
"""
import argparse
import os
import subprocess
import sys
import json


def load_config():
    """Load config.json from the project root."""
    # Find the project root by going up from current directory until we find config.json
    current_dir = os.path.dirname(os.path.abspath(__file__))
    for _ in range(10):  # Max 10 levels up
        config_path = os.path.join(current_dir, "config.json")
        if os.path.exists(config_path):
            try:
                with open(config_path, 'r', encoding='utf-8') as f:
                    config = json.load(f)
                print(f"[DEBUG] Config loaded from: {config_path}", file=sys.stderr)
                print(f"[DEBUG] PathConfigs: {config.get('PathConfigs', [])}", file=sys.stderr)
                return config
            except Exception as e:
                print(f"[ERROR] Failed to load config.json: {e}", file=sys.stderr)
                return {}
        parent = os.path.dirname(current_dir)
        if parent == current_dir:
            break
        current_dir = parent
    print("[ERROR] config.json not found", file=sys.stderr)
    return {}


def get_path_from_config(config, name):
    """Get the last valid path for a given name from PathConfigs."""
    path_configs = config.get("PathConfigs", [])
    for pc in path_configs:
        if pc.get("Name", "").lower() == name.lower():
            paths = pc.get("Paths", [])
            # Return the last non-empty path
            for path in reversed(paths):
                if path and path.strip():
                    return path.strip()
    return ""


def main():
    parser = argparse.ArgumentParser(description="Batch wrapper to call Python scripts with named args")
    parser.add_argument('--target', required=True, help='Target script filename (in same Python folder)')
    parser.add_argument('--pilihan', required=True, help='Pilihan path')
    parser.add_argument('--master', required=False, default='', help='Master path (Primary)')
    parser.add_argument('--master2', required=False, default='', help='Master path (Secondary/Sporty/8R)')
    parser.add_argument('--output', required=False, default='', help='Output path')
    parser.add_argument('--okebase', required=False, default='', help='Oke Base Path')

    args, unknown = parser.parse_known_args()

    base_dir = os.path.dirname(os.path.realpath(__file__))
    
    # Search for target script - Prioritize subfolders!
    possible_paths = [
        os.path.join(base_dir, "Master", args.target),
        os.path.join(base_dir, "Action", args.target),
        os.path.join(base_dir, args.target)
    ]
    
    target_path = ""
    for p in possible_paths:
            target_path = p
            print(f"[DEBUG_WRAPPER] FOUND TARGET: {target_path}", file=sys.stderr)
            break
    
    print(f"[DEBUG_WRAPPER] RESOLVED TARGET PATH: {target_path}", file=sys.stderr)
            
    if not target_path:
        print(f"ERROR: Target script not found: {args.target}", file=sys.stderr)
        return 2

    config = load_config()

    # Build command based on target script
    if args.target == 'pasfoto.py':
        # pasfoto.py <master> <pilihan> <output> <oke_base>
        oke_base = args.okebase
        if not oke_base:
            oke_base = get_path_from_config(config, "OKE BASE")
        if not oke_base:
            oke_base = args.output  # fallback to output
        
        # Ensure we pass 4 args strictly as expected by pasfoto.py
        cmd = [sys.executable, target_path, args.master, args.pilihan, args.output, oke_base]

    elif args.target == 'manasik.py':
        # manasik.py <master> <pilihan> <output> <master2> <okebase>
        cmd = [sys.executable, target_path, args.master, args.pilihan, args.output, args.master2, args.okebase]
        
    elif args.target == 'wisuda.py':
        # wisuda.py <master> <pilihan> <output> <master2> <okebase>
        cmd = [sys.executable, target_path, args.master, args.pilihan, args.output, args.master2, args.okebase]
    elif args.target == 'profesi_flat.py':
        # profesi_flat.py <master_profesi> <master_sporty> <pilihan> <output> <oke_base>
        master_profesi = args.master
        master_sporty = args.master2 
        
        # Fallback to Config lookup if master2 is empty (backward compatibility)
        if not master_sporty:
             path_config = None
             for pc in config.get("PathConfigs", []):
                 if pc.get("Name") == "MD OB PROFESI DAN SPORTY":
                     path_config = pc
                     break
             if path_config:
                 paths = path_config.get("Paths", [])
                 if len(paths) >= 2:
                     master_sporty = paths[1]
                 elif len(paths) >= 1:
                     master_sporty = paths[0]

        print(f"[DEBUG] master_profesi: '{master_profesi}'", file=sys.stderr)
        print(f"[DEBUG] master_sporty: '{master_sporty}'", file=sys.stderr)
        
        oke_base = args.okebase
        if not oke_base:
             oke_base = get_path_from_config(config, "OKE BASE")
        if not oke_base:
            oke_base = args.output
        
        cmd = [sys.executable, target_path, master_profesi, master_sporty, args.pilihan, args.output, oke_base]
    else:
        print(f"ERROR: Unknown target script: {args.target}", file=sys.stderr)
        return 4

    try:
        # Prepare environment
        env = os.environ.copy()
        env["PYTHONIOENCODING"] = "utf-8"
        env["PYTHONUNBUFFERED"] = "1"

        print(f"[DEBUG_WRAPPER] Launching subprocess: {cmd}", file=sys.stderr)
        
        # Use simple Popen without text=True to avoid buffering issues, handle decoding manually
        proc = subprocess.Popen(
            cmd, 
            stdout=subprocess.PIPE, 
            stderr=subprocess.STDOUT, # Merge stderr into stdout to avoid deadlock
            env=env
        )

        while True:
            # Read byte by byte or line by line
            line = proc.stdout.readline()
            if not line:
                if proc.poll() is not None:
                    break
                continue
            
            # Decode and print
            try:
                decoded_line = line.decode('utf-8', errors='replace')
                sys.stdout.write(decoded_line)
                sys.stdout.flush()
            except:
                 pass

        return proc.returncode
    except Exception as e:
        print(f"ERROR: Exception running target script: {e}", file=sys.stderr)
        return 3


if __name__ == '__main__':
    sys.exit(main())
