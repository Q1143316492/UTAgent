"""L1 helper：查 WndDemo 计数（E03 幂等断言）。"""
import json
import unity

r = unity.find_objects("WndDemo", echo=False)
print(json.dumps({"count": r.get("count")}, ensure_ascii=False))
