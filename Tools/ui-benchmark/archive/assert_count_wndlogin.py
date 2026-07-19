import json
import unity

r = unity.find_objects("WndLogin", echo=False)
print(json.dumps({"count": r.get("count", 0), "names": r.get("names", [])}, ensure_ascii=False))
