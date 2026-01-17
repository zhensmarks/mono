import argparse
import os
import sys
import json
import traceback

# Add core directory to path for pixelcut import
script_dir = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, script_dir)

from pixelcut import Pixelcut

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--action", required=True, choices=["upscale", "remove_bg"])
    parser.add_argument("--input", required=True)
    parser.add_argument("--proxy", default=None)
    
    args = parser.parse_args()

    if not os.path.exists(args.input):
        print(f"ERROR: File tidak ditemukan: {args.input}", file=sys.stderr)
        sys.exit(1)

    # Determine output directory (Same as input)
    output_path = os.path.dirname(args.input)
    file_name = os.path.basename(args.input)
    name_no_ext = os.path.splitext(file_name)[0]

    # Construct payload expected by Pixelcut class (same as main.py)
    payload = {
        'job': args.action,
        'file': args.input,
        'output_path': output_path,
        'name': name_no_ext,
        'output': '',  # Will be set by the pixelcut functions
        'id': 'cli_job',
        'item': None
    }
    
    try:
        # Initialize Processor (like main.py does)
        processor = Pixelcut(payload=payload, proxy=args.proxy)
        
        # Patch is_running (CRITICAL! - this is what main.py does)
        processor.is_running = lambda: True
        
        # Patch _process to output to console (like main.py's queue pattern)
        def patched_process(message):
            print(f"SIGNAL:process:{json.dumps({'status': str(message)})}", flush=True)
        processor._process = patched_process
        
        # Check write permissions
        if not os.access(output_path, os.W_OK):
             print(f"ERROR: Tidak ada izin menulis di folder output: {output_path}", file=sys.stderr)
             sys.exit(1)

        # Run the job
        if args.action == "upscale":
            processor.upscale()
        else:
            processor.remove_bg()
        
        # Check output
        out_png = os.path.join(output_path, f"{name_no_ext}.png")
        if args.action == "upscale":
            out_png = os.path.join(output_path, f"{name_no_ext}_up.png")
            
        if os.path.exists(out_png):
            print("SIGNAL:rendered:done", flush=True)
            print("Selesai!", flush=True)
        else:
            print(f"ERROR: Output tidak ditemukan: {out_png}", file=sys.stderr)
            sys.exit(1)
            
    except Exception as e:
        tb = traceback.format_exc()
        print(f"ERROR: {str(e)}", file=sys.stderr)
        print(f"Traceback:\n{tb}", file=sys.stderr)
        sys.exit(1)
