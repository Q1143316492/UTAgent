import json
import unity
sr = unity.save_scene()
print(json.dumps({"variant": "B_save_only", "save": sr}, ensure_ascii=False))
