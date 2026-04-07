import json
import sys

output_path = sys.argv[1]

data = {"hello": "world"}

with open(output_path, "w") as f:
    json.dump(data, f, indent=2)

print("hello_world.json written successfully")