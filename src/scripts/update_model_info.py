import os
import re
import json
import sys


# Lookup table mapping C# class names to LiteLLM provider prefixes
PROVIDER_LOOKUP = {
    "AnthropicProvider": "anthropic",
    "OpenAiProvider":    "openai",
    "GroqProvider":      "groq",
    # add new providers here as they are implemented
}


# get the provider names of all concrete provider classes
def find_concrete_provider_files(src_dir):
    provider_names = set()
    
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
            
            if has_abstract:
                continue

            provider_name = re.search(
                r'public\s+override\s+string\s+ProviderName\s*=>\s*"(\w+)"',
                content
            )
            
            if provider_name:
                provider_names.add(provider_name.group(1))
            else:
                print(f"##[warning]{file} has no ProviderName property.")
    
    return provider_names

# MAIN ==================================================================================================

output_path = sys.argv[1]
script_dir = os.path.dirname(os.path.abspath(__file__))
provider_folder_path = os.path.join(script_dir, "..", "Physalia.Core", "Providers")

concrete_providers = find_concrete_provider_files(provider_folder_path)

data = {"NUM_PROVIDERS": f"{len(concrete_providers)}"}

with open(output_path, "w") as f:
    json.dump(data, f, indent=2)

print("hello_world.json written successfully")