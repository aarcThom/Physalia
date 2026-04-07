import os
import re
import json
import sys

# get all concrete classes
def find_concrete_provider_files(src_dir):
    concrete_files = set()
    
    for root, dirs, files in os.walk(src_dir):
        for file in files:
            if not file.endswith(".cs"):
                continue
            filepath = os.path.join(root, file)
            with open(filepath, "r", encoding="utf-8") as f:
                content = f.read()
            
            has_abstract = re.search(
                r'^\s*(?:public|internal|private)\s+abstract\s+class\s+\w+',
                content,
                re.MULTILINE
            )
            
            if not has_abstract:
                concrete_files.add(file)
    
    return concrete_files

# MAIN ==================================================================================================

output_path = sys.argv[1]
script_dir = os.path.dirname(os.path.abspath(__file__))
provider_folder_path = os.path.join(script_dir, "..", "Physalia.Core", "Providers")

concrete_providers = find_concrete_provider_files(provider_folder_path)

data = {"NUM_PROVIDERS": f"{len(concrete_providers)}"}

with open(output_path, "w") as f:
    json.dump(data, f, indent=2)

print("hello_world.json written successfully")